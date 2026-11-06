import { expect, it } from 'vitest';
import { parsePrice } from './price';

it.each([
  ['350', 35000], ['350,50', 35050], ['350.5', 35050],
  [' 0,01 ', 1], ['1.01', 101], ['1000000', 100000000],
])('переводит %s рублей в %i копеек', (input, expected) => {
  expect(parsePrice(input)).toBe(expected);
});

it.each(['', ' ', '0', '-1', '1.001', '1,2.3', '1e3', 'Infinity', 'текст', '1000000.01'])('отклоняет неверную цену %s', input => {
  expect(parsePrice(input)).toBeNull();
});
