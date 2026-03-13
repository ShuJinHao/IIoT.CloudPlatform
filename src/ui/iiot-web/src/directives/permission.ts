// src/directives/permission.ts
// 🌟 v-permission 按钮级权限控制指令
// 用法：
//   <button v-permission="'Employee.Onboard'">入职</button>
//   <button v-permission="['Employee.Update', 'Employee.Deactivate']">编辑</button>

import type { Directive, DirectiveBinding } from 'vue';
import { useAuthStore } from '../stores/auth';

type PermissionValue = string | string[];

const permission: Directive<HTMLElement, PermissionValue> = {
  mounted(el: HTMLElement, binding: DirectiveBinding<PermissionValue>) {
    _check(el, binding.value);
  },
  updated(el: HTMLElement, binding: DirectiveBinding<PermissionValue>) {
    _check(el, binding.value);
  },
};

function _check(el: HTMLElement, value: PermissionValue) {
  const authStore = useAuthStore();

  const required = Array.isArray(value) ? value : [value];
  const hasAccess = authStore.hasAllPermissions(required);

  if (!hasAccess) {
    // 无权限：隐藏元素（比 remove 更安全，不会破坏 DOM 结构）
    el.style.display = 'none';
    el.setAttribute('aria-hidden', 'true');
  } else {
    el.style.display = '';
    el.removeAttribute('aria-hidden');
  }
}

export default permission;
