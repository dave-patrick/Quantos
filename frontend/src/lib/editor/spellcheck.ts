import { StateEffect, StateField, type Extension } from '@codemirror/state';
import { Decoration, EditorView, type DecorationSet, ViewPlugin, type ViewUpdate } from '@codemirror/view';
import { RangeSetBuilder } from '@codemirror/state';

export const toggleSpellCheck = StateEffect.define<boolean>();

const spellCheckEnabled = StateField.define<boolean>({
  create() { return false; },
  update(value: boolean, tr) {
    for (const effect of tr.effects) if (effect.is(toggleSpellCheck)) return effect.value;
    return value;
  }
});

const WORD = /[\p{L}0-9]+(?:'[\p{L}0-9]+)*/gu;

const DICTIONARY = new Set([
  'a','about','above','across','after','again','against','all','almost','alone','along','already','also','although','always','among','an','and','another','any','anybody','anyone','anything','anywhere','apart','are','around','as','at','away','back','became','because','become','becomes','becoming','been','before','beforehand','behind','being','below','beside','besides','between','beyond','both','brief','but','by','can','cannot','could','couldnt','did','do','does','doing','don','down','due','during','each','eg','eight','either','eleven','else','elsewhere','empty','enough','even','ever','every','everyone','everything','everywhere','except','few','fifteen','fify','fill','find','fire','first','five','for','former','formerly','forty','found','four','from','front','full','further','get','give','go','had','has','hasnt','have','he','hence','her','here','hereafter','hereby','herein','hereupon','hers','herself','him','himself','his','how','however','hundred','i','ie','if','in','inc','indeed','interest','into','is','it','its','itself','keep','last','latter','latterly','least','less','just','like','likes','made','make','many','may','me','meanwhile','might','mill','mine','more','moreover','most','mostly','move','much','must','my','myself','name','namely','neither','never','nevertheless','next','nine','no','nobody','none','noone','nor','not','nothing','now','nowhere','of','off','often','on','once','one','only','onto','or','other','others','otherwise','our','ours','ourselves','out','over','own','part','per','perhaps','place','please','put','rather','re','same','see','seem','seemed','seeming','seems','serious','several','she','should','show','side','since','sincere','six','sixty','so','some','somebody','somehow','someone','something','sometimes','somewhere','still','such','take','ten','than','that','the','their','them','themselves','then','thence','there','thereafter','thereby','therefore','therein','thereupon','these','they','thick','thin','third','this','those','though','three','through','throughout','thru','thus','to','together','too','top','toward','towards','twelve','twenty','two','un','under','unless','unlike','until','up','upon','us','use','used','using','various','very','via','was','we','well','were','what','whatever','when','whence','whenever','where','whereafter','whereas','whereby','wherein','whereupon','wherever','whether','which','while','whither','who','whoever','whole','whom','whose','why','will','with','within','without','would','yet','you','your','yours','yourself','yourselves'
]);

function isSkipLine(text: string): boolean {
  return text.startsWith('```') || text.startsWith('~~~') || text.startsWith('> ') || text.startsWith('|') || /^\s*[-*+]\s/.test(text) || /^\s*\d+\.\s/.test(text);
}

function build(view: EditorView): DecorationSet {
  const builder = new RangeSetBuilder<Decoration>();
  for (const { from, to } of view.visibleRanges) {
    for (let pos = from; pos < to; ) {
      const line = view.state.doc.lineAt(pos);
      const text = line.text;
      if (isSkipLine(text)) { pos = line.to + 1; continue; }
      WORD.lastIndex = 0;
      let match: RegExpExecArray | null;
      while ((match = WORD.exec(text)) !== null) {
        const word = match[0].toLowerCase();
        if (word.length < 3 || DICTIONARY.has(word)) continue;
        builder.add(line.from + match.index, line.from + match.index + match[0].length, Decoration.mark({ class: 'cm-spell-error' }));
      }
      pos = line.to + 1;
    }
  }
  return builder.finish();
}

const spellCheckPlugin = ViewPlugin.fromClass(
  class {
    decorations: DecorationSet;
    constructor(view: EditorView) { this.decorations = view.state.field(spellCheckEnabled) ? build(view) : Decoration.none; }
    update(u: ViewUpdate) {
      if (u.docChanged || u.viewportChanged || u.startState.field(spellCheckEnabled) !== u.state.field(spellCheckEnabled)) {
        this.decorations = u.state.field(spellCheckEnabled) ? build(u.view) : Decoration.none;
      }
    }
  },
  { decorations: (v) => v.decorations }
);

export function spellCheckExtension(): Extension[] {
  return [spellCheckEnabled, spellCheckPlugin];
}
