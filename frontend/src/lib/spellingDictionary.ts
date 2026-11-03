import nspell from 'nspell';
import usAff from '../../node_modules/dictionary-en/index.aff?raw';
import usDic from '../../node_modules/dictionary-en/index.dic?raw';
import gbAff from '../../node_modules/dictionary-en-gb/index.aff?raw';
import gbDic from '../../node_modules/dictionary-en-gb/index.dic?raw';

const us = nspell(usAff, usDic);
const gb = nspell(gbAff, gbDic);
const cache = new Map<string, boolean>();

export function correct(text: string): boolean {
  const word = text.replaceAll('’', "'");
  const cached = cache.get(word);
  if (cached !== undefined) return cached;
  const valid = us.correct(word) || gb.correct(word) || word === 'Quantos';
  if (cache.size >= 20000) cache.clear();
  cache.set(word, valid);
  return valid;
}
