import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';

const [typescriptModule, expectedCommand, expectedArgument, expectedProjectPath] = process.argv.slice(2);
if (!typescriptModule || !expectedCommand || !expectedArgument || !expectedProjectPath) {
  throw new Error('usage: node Assert-JavaScriptSpawnProjectConsumer.mjs <typescript.js> <command> <argument> <projectPath>');
}

const require = createRequire(import.meta.url);
const ts = require(resolve(typescriptModule));
const source = readFileSync(0, 'utf8');
const fileName = '/virtual/cloud-support-consumer.mjs';
const options = {
  allowJs: true,
  checkJs: false,
  module: ts.ModuleKind.ESNext,
  noLib: true,
  noResolve: true,
  target: ts.ScriptTarget.ESNext,
};
const sourceFile = ts.createSourceFile(fileName, source, ts.ScriptTarget.ESNext, true, ts.ScriptKind.JS);
const host = ts.createCompilerHost(options, true);
host.fileExists = (candidate) => candidate === fileName;
host.readFile = (candidate) => candidate === fileName ? source : undefined;
host.getSourceFile = (candidate) => candidate === fileName ? sourceFile : undefined;
host.writeFile = () => {};
const program = ts.createProgram([fileName], options, host);
const parsed = program.getSourceFile(fileName);
if (!parsed || parsed.parseDiagnostics.length > 0) {
  const details = parsed?.parseDiagnostics.map((diagnostic) => diagnostic.messageText).join('; ') ?? 'missing source';
  throw new Error(`JavaScript AST parse failed: ${details}`);
}

const checker = program.getTypeChecker();
const spawnImports = new Set();
const childProcessNamespaces = new Set();
const resolveImports = new Set();
const pathNamespaces = new Set();
const dirnameImports = new Set();
const fileUrlToPathImports = new Set();
const urlNamespaces = new Set();
for (const statement of parsed.statements) {
  if (!ts.isImportDeclaration(statement) ||
      !ts.isStringLiteral(statement.moduleSpecifier)) {
    continue;
  }

  const moduleName = statement.moduleSpecifier.text;
  const bindings = statement.importClause?.namedBindings;
  if (['node:child_process', 'child_process'].includes(moduleName) &&
      bindings && ts.isNamedImports(bindings)) {
    for (const element of bindings.elements) {
      const importedName = element.propertyName?.text ?? element.name.text;
      if (importedName === 'spawn') {
        const symbol = checker.getSymbolAtLocation(element.name);
        if (symbol) spawnImports.add(symbol);
      }
    }
  } else if (['node:child_process', 'child_process'].includes(moduleName) &&
             bindings && ts.isNamespaceImport(bindings)) {
    const symbol = checker.getSymbolAtLocation(bindings.name);
    if (symbol) childProcessNamespaces.add(symbol);
  }

  if (['node:path', 'path'].includes(moduleName) && bindings && ts.isNamedImports(bindings)) {
    for (const element of bindings.elements) {
      const importedName = element.propertyName?.text ?? element.name.text;
      if (importedName === 'resolve') {
        const symbol = checker.getSymbolAtLocation(element.name);
        if (symbol) resolveImports.add(symbol);
      }
      if (importedName === 'dirname') {
        const symbol = checker.getSymbolAtLocation(element.name);
        if (symbol) dirnameImports.add(symbol);
      }
    }
  } else if (['node:path', 'path'].includes(moduleName) &&
             bindings && ts.isNamespaceImport(bindings)) {
    const symbol = checker.getSymbolAtLocation(bindings.name);
    if (symbol) pathNamespaces.add(symbol);
  }

  if (['node:url', 'url'].includes(moduleName) && bindings && ts.isNamedImports(bindings)) {
    for (const element of bindings.elements) {
      const importedName = element.propertyName?.text ?? element.name.text;
      if (importedName === 'fileURLToPath') {
        const symbol = checker.getSymbolAtLocation(element.name);
        if (symbol) fileUrlToPathImports.add(symbol);
      }
    }
  } else if (['node:url', 'url'].includes(moduleName) &&
             bindings && ts.isNamespaceImport(bindings)) {
    const symbol = checker.getSymbolAtLocation(bindings.name);
    if (symbol) urlNamespaces.add(symbol);
  }
}

const symbolOf = (node) => checker.getSymbolAtLocation(node);
const unparenthesized = (node) => {
  while (ts.isParenthesizedExpression(node)) node = node.expression;
  return node;
};
const literalText = (node) => {
  if (!node) return undefined;
  node = unparenthesized(node);
  return ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node) ? node.text : undefined;
};
const staticBoolean = (node) => {
  if (!node) return undefined;
  node = unparenthesized(node);
  if (node.kind === ts.SyntaxKind.TrueKeyword) return true;
  if (node.kind === ts.SyntaxKind.FalseKeyword) return false;
  if (ts.isPrefixUnaryExpression(node) && node.operator === ts.SyntaxKind.ExclamationToken) {
    const value = staticBoolean(node.operand);
    return value === undefined ? undefined : !value;
  }
  return undefined;
};
const isImportedSpawn = (expression) => {
  expression = unparenthesized(expression);
  if (ts.isIdentifier(expression)) {
    const symbol = symbolOf(expression);
    return symbol !== undefined && spawnImports.has(symbol);
  }
  if (ts.isPropertyAccessExpression(expression) && expression.name.text === 'spawn') {
    const namespace = unparenthesized(expression.expression);
    const symbol = ts.isIdentifier(namespace) ? symbolOf(namespace) : undefined;
    return symbol !== undefined && childProcessNamespaces.has(symbol);
  }
  return false;
};
const isImportedResolve = (expression) => {
  expression = unparenthesized(expression);
  if (ts.isIdentifier(expression)) {
    const symbol = symbolOf(expression);
    return symbol !== undefined && resolveImports.has(symbol);
  }
  if (ts.isPropertyAccessExpression(expression) && expression.name.text === 'resolve') {
    const namespace = unparenthesized(expression.expression);
    const symbol = ts.isIdentifier(namespace) ? symbolOf(namespace) : undefined;
    return symbol !== undefined && pathNamespaces.has(symbol);
  }
  return false;
};
const isImportedMember = (expression, directImports, namespaces, memberName) => {
  expression = unparenthesized(expression);
  if (ts.isIdentifier(expression)) {
    const symbol = symbolOf(expression);
    return symbol !== undefined && directImports.has(symbol);
  }
  if (ts.isPropertyAccessExpression(expression) && expression.name.text === memberName) {
    const namespace = unparenthesized(expression.expression);
    const symbol = ts.isIdentifier(namespace) ? symbolOf(namespace) : undefined;
    return symbol !== undefined && namespaces.has(symbol);
  }
  return false;
};
const isImportedDirname = (expression) =>
  isImportedMember(expression, dirnameImports, pathNamespaces, 'dirname');
const isImportedFileUrlToPath = (expression) =>
  isImportedMember(expression, fileUrlToPathImports, urlNamespaces, 'fileURLToPath');
const hasParentTraversal = (call) => call.arguments.some((argument) => {
  const value = literalText(argument);
  return value !== undefined && value.split(/[\\/]+/u).includes('..');
});
const constInitializers = new Map();
for (const statement of parsed.statements) {
  if (!ts.isVariableStatement(statement) ||
      (statement.declarationList.flags & ts.NodeFlags.Const) === 0) continue;
  for (const declaration of statement.declarationList.declarations) {
    if (!ts.isIdentifier(declaration.name) || !declaration.initializer) continue;
    const symbol = symbolOf(declaration.name);
    if (symbol) constInitializers.set(symbol, declaration.initializer);
  }
}
const isImportMetaUrl = (node) => {
  node = unparenthesized(node);
  return ts.isPropertyAccessExpression(node) && node.name.text === 'url' &&
    ts.isMetaProperty(node.expression) &&
    node.expression.keywordToken === ts.SyntaxKind.ImportKeyword &&
    node.expression.name.text === 'meta';
};
const isRelativeLiteral = (node) => {
  const value = literalText(node);
  return value !== undefined && value.length > 0 &&
    !value.startsWith('/') && !value.startsWith('\\\\') && !/^[A-Za-z]:[\\/]/u.test(value);
};
const isTrustedScriptPath = (node, visitedSymbols = new Set()) => {
  node = unparenthesized(node);
  if (ts.isIdentifier(node)) {
    const symbol = symbolOf(node);
    if (!symbol || visitedSymbols.has(symbol) || !constInitializers.has(symbol)) return false;
    visitedSymbols.add(symbol);
    const trusted = isTrustedScriptPath(constInitializers.get(symbol), visitedSymbols);
    visitedSymbols.delete(symbol);
    return trusted;
  }
  if (!ts.isCallExpression(node)) return false;
  if (isImportedFileUrlToPath(node.expression)) {
    return node.arguments.length === 1 && isImportMetaUrl(node.arguments[0]);
  }
  if (isImportedDirname(node.expression)) {
    return node.arguments.length === 1 && isTrustedScriptPath(node.arguments[0], visitedSymbols);
  }
  if (isImportedResolve(node.expression)) {
    return node.arguments.length >= 2 &&
      isTrustedScriptPath(node.arguments[0], visitedSymbols) &&
      node.arguments.slice(1).every(isRelativeLiteral);
  }
  return false;
};
const validatedRepoRoots = new Set();
for (const statement of parsed.statements) {
  if (!ts.isVariableStatement(statement) ||
      (statement.declarationList.flags & ts.NodeFlags.Const) === 0) continue;
  for (const declaration of statement.declarationList.declarations) {
    if (!ts.isIdentifier(declaration.name) || declaration.name.text !== 'repoRoot' ||
        !declaration.initializer) continue;
    const initializer = unparenthesized(declaration.initializer);
    if (!ts.isCallExpression(initializer) ||
        !isImportedResolve(initializer.expression) ||
        !hasParentTraversal(initializer) ||
        !isTrustedScriptPath(initializer)) continue;
    const symbol = symbolOf(declaration.name);
    if (symbol) validatedRepoRoots.add(symbol);
  }
}
const isExpectedProjectExpression = (node) => {
  node = unparenthesized(node);
  const callee = ts.isCallExpression(node) ? unparenthesized(node.expression) : undefined;
  return ts.isCallExpression(node) &&
    callee && isImportedResolve(callee) &&
    node.arguments.length >= 2 &&
    ts.isIdentifier(unparenthesized(node.arguments[0])) &&
    validatedRepoRoots.has(symbolOf(unparenthesized(node.arguments[0]))) &&
    literalText(node.arguments[1]) === expectedProjectPath;
};
const isExpectedSpawn = (call) => {
  if (!isImportedSpawn(call.expression) || literalText(call.arguments[0]) !== expectedCommand) return false;
  const args = call.arguments[1] && unparenthesized(call.arguments[1]);
  if (!args || !ts.isArrayLiteralExpression(args)) return false;
  for (let index = 0; index + 1 < args.elements.length; index += 1) {
    if (literalText(args.elements[index]) === expectedArgument &&
        isExpectedProjectExpression(args.elements[index + 1])) return true;
  }
  return false;
};

let found = false;
const visitedFunctions = new Set();
let visitStatement;
let visitExpression;
const visitFunction = (node) => {
  if (visitedFunctions.has(node)) return;
  visitedFunctions.add(node);
  if (ts.isBlock(node.body)) visitStatement(node.body);
  else visitExpression(node.body);
};
const visitCallableSymbol = (expression) => {
  expression = unparenthesized(expression);
  if (ts.isArrowFunction(expression) || ts.isFunctionExpression(expression)) {
    visitFunction(expression);
    return;
  }
  if (!ts.isIdentifier(expression) && !ts.isPropertyAccessExpression(expression)) return;
  const symbol = symbolOf(ts.isPropertyAccessExpression(expression) ? expression.name : expression);
  for (const declaration of symbol?.declarations ?? []) {
    if (ts.isFunctionDeclaration(declaration) && declaration.body) visitFunction(declaration);
    if (ts.isVariableDeclaration(declaration) && declaration.initializer &&
        (ts.isArrowFunction(declaration.initializer) || ts.isFunctionExpression(declaration.initializer))) {
      visitFunction(declaration.initializer);
    }
  }
};
const isIntrinsicPromiseConstructor = (expression) => {
  expression = unparenthesized(expression);
  if (!ts.isIdentifier(expression) || expression.text !== 'Promise') return false;
  const symbol = symbolOf(expression);
  return symbol === undefined || (symbol.declarations ?? []).length === 0;
};
visitExpression = (node) => {
  if (!node || found) return;
  node = unparenthesized(node);
  if (ts.isArrowFunction(node) || ts.isFunctionExpression(node)) return;
  if (ts.isConditionalExpression(node)) {
    visitExpression(node.condition);
    const value = staticBoolean(node.condition);
    if (value !== false) visitExpression(node.whenTrue);
    if (value !== true) visitExpression(node.whenFalse);
    return;
  }
  if (ts.isBinaryExpression(node) &&
      (node.operatorToken.kind === ts.SyntaxKind.AmpersandAmpersandToken ||
       node.operatorToken.kind === ts.SyntaxKind.BarBarToken)) {
    visitExpression(node.left);
    const value = staticBoolean(node.left);
    if ((node.operatorToken.kind === ts.SyntaxKind.AmpersandAmpersandToken && value !== false) ||
        (node.operatorToken.kind === ts.SyntaxKind.BarBarToken && value !== true)) visitExpression(node.right);
    return;
  }
  if (ts.isCallExpression(node) || ts.isNewExpression(node)) {
    if (ts.isCallExpression(node) && isExpectedSpawn(node)) {
      found = true;
      return;
    }
    visitExpression(node.expression);
    visitCallableSymbol(node.expression);
    for (const argument of node.arguments ?? []) {
      visitExpression(argument);
    }
    if (ts.isNewExpression(node) && isIntrinsicPromiseConstructor(node.expression) &&
        node.arguments?.length) {
      const executor = unparenthesized(node.arguments[0]);
      if (ts.isArrowFunction(executor) || ts.isFunctionExpression(executor)) visitFunction(executor);
    }
    return;
  }
  ts.forEachChild(node, (child) => visitExpression(child));
};
visitStatement = (node) => {
  if (!node || found) return true;
  if (ts.isFunctionDeclaration(node) || ts.isClassDeclaration(node)) return true;
  if (ts.isBlock(node) || ts.isSourceFile(node)) {
    let mayContinue = true;
    for (const statement of node.statements) {
      if (!mayContinue || found) break;
      mayContinue = visitStatement(statement);
    }
    return mayContinue;
  }
  if (ts.isIfStatement(node)) {
    visitExpression(node.expression);
    const value = staticBoolean(node.expression);
    if (value === true) return visitStatement(node.thenStatement);
    if (value === false) return node.elseStatement ? visitStatement(node.elseStatement) : true;
    const thenContinues = visitStatement(node.thenStatement);
    const elseContinues = node.elseStatement ? visitStatement(node.elseStatement) : true;
    return thenContinues || elseContinues;
  }
  if (ts.isWhileStatement(node) || ts.isDoStatement(node) || ts.isForStatement(node)) {
    if (node.initializer) visitExpression(node.initializer);
    if (node.condition) visitExpression(node.condition);
    const condition = node.expression ?? node.condition;
    if (staticBoolean(condition) !== false) visitStatement(node.statement);
    if (node.incrementor) visitExpression(node.incrementor);
    return true;
  }
  if (ts.isTryStatement(node)) {
    const tryContinues = visitStatement(node.tryBlock);
    const catchContinues = node.catchClause ? visitStatement(node.catchClause.block) : false;
    const finallyContinues = node.finallyBlock ? visitStatement(node.finallyBlock) : true;
    return finallyContinues && (tryContinues || catchContinues);
  }
  if (ts.isVariableStatement(node)) {
    for (const declaration of node.declarationList.declarations) {
      if (declaration.initializer) visitExpression(declaration.initializer);
    }
    return true;
  }
  if (ts.isReturnStatement(node) || ts.isThrowStatement(node)) {
    visitExpression(node.expression);
    return false;
  }
  if (ts.isExpressionStatement(node)) {
    visitExpression(node.expression);
    return true;
  }
  ts.forEachChild(node, (child) => {
    if (ts.isExpression(child)) visitExpression(child);
    else visitStatement(child);
  });
  return true;
};

visitStatement(parsed);
process.exitCode = found ? 0 : 1;
