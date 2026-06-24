import { mount } from '@vue/test-utils';
import { defineComponent, h } from 'vue';
import { describe, expect, it } from 'vitest';
import UiDataTable from './UiDataTable.vue';
import type { UiDataTableColumn } from './types';

type TestRow = {
  id: string;
  name: string;
  count: number;
};

const rows: object[] = [
  { id: 'r1', name: '设备 A', count: 3 },
];

describe('UiDataTable', () => {
  it('renders default cells and formatter cells', () => {
    const columns: UiDataTableColumn<object>[] = [
      { title: '名称', key: 'name' },
      {
        title: '数量',
        key: 'count',
        formatter: (value) => `${value} 台`,
      },
    ];

    const wrapper = mount(UiDataTable, {
      props: {
        columns,
        data: rows,
        rowKey: 'id',
      },
    });

    expect(wrapper.text()).toContain('设备 A');
    expect(wrapper.text()).toContain('3 台');
  });

  it('renders component cells with context props', () => {
    const CountCell = defineComponent({
      props: {
        value: { type: Number, required: true },
        unit: { type: String, required: true },
      },
      setup(props) {
        return () => h('strong', `${props.value}${props.unit}`);
      },
    });
    const columns: UiDataTableColumn<object>[] = [
      {
        title: '数量',
        key: 'count',
        component: CountCell,
        componentProps: () => ({ unit: '件' }),
      },
    ];

    const wrapper = mount(UiDataTable, {
      props: {
        columns,
        data: rows,
      },
    });

    expect(wrapper.text()).toContain('3件');
  });

  it('renders named slots with row context', () => {
    const columns: UiDataTableColumn<object>[] = [
      { title: '名称', key: 'name', slot: 'nameCell' },
    ];

    const wrapper = mount(UiDataTable, {
      props: {
        columns,
        data: rows,
      },
      slots: {
        nameCell: ({ row }: { row: object }) => h('em', (row as TestRow).name),
      },
    });

    expect(wrapper.find('em').text()).toBe('设备 A');
  });
});
