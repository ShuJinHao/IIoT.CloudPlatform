using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class RecipeStatusUpgrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_active",
                table: "recipes");

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "recipes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_recipes_name_version",
                table: "recipes",
                columns: new[] { "recipe_name", "version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_recipes_name_version",
                table: "recipes");

            migrationBuilder.DropColumn(
                name: "status",
                table: "recipes");

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "recipes",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
