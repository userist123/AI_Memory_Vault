import { describe, expect, it } from 'vitest';
import { camelToSnake, snakeToCamel } from '@/index.js';

describe('snakeToCamel', () => {
  it('converts snake_case string to camelCase', () => {
    expect(snakeToCamel('foo_bar_baz')).toBe('fooBarBaz');
  });

  it('preserves leading underscore behavior from implementation', () => {
    expect(snakeToCamel('_private_value')).toBe('_privateValue');
  });

  it('preserves trailing underscore', () => {
    expect(snakeToCamel('the_variable_')).toBe('theVariable_');
  });

  it('normalizes uppercase input to lower camel case', () => {
    expect(snakeToCamel('FOO_BAR')).toBe('fooBar');
  });

  it('supports multiple consecutive underscores between segments', () => {
    expect(snakeToCamel('foo__bar___baz')).toBe('fooBarBaz');
  });

  it('does not change already camel-case strings except lowercasing first', () => {
    expect(snakeToCamel('alreadyCamel')).toBe('alreadycamel');
  });
});

describe('camelToSnake', () => {
  it('converts camelCase string to snake_case', () => {
    expect(camelToSnake('fooBarBaz')).toBe('foo_bar_baz');
  });

  it('preserves leading underscore behavior from implementation', () => {
    expect(camelToSnake('_privateValue')).toBe('_private_value');
  });

  it('preserves trailing underscore', () => {
    expect(camelToSnake('theVariable_')).toBe('the_variable_');
  });

  it('uppercase input to lower snake_case', () => {
    expect(camelToSnake('FOO_BAR')).toBe('_f_o_o__b_a_r');
  });
});
