/**
 * Converts a snake_case string to camelCase.
 * @example
 *  snakeToCamel('foo_bar_baz') // 'fooBarBaz'
 *  snakeToCamel('_private_value') // '_privateValue'
 *  snakeToCamel('the_variable_') // 'theVariable_'
 *  snakeToCamel('alreadyCamel') // 'alreadycamel'
 */
export function snakeToCamel(str: string): string {
  return str
    .toLowerCase()
    .replace(/(?!^)_+([a-z0-9])/g, (_, ch) => ch.toUpperCase());
}

/**
 * Converts a camelCase string to snake_case.
 * @example
 * camelToSnake('fooBarBaz') // 'foo_bar_baz'
 * camelToSnake('_privateValue') // '_private_value'
 * camelToSnake('theVariable_') // 'the_variable_'
 * camelToSnake('alreadycamel') // 'alreadycamel'
 */
export function camelToSnake(str: string): string {
  return str.replace(/[A-Z]/g, (match) => `_${match.toLowerCase()}`);
}
