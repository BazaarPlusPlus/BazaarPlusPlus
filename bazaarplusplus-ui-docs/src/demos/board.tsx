import { Board, SkillStrip, gameTerms } from '@bpp/ui';
import { card, pinned, pinnedSkills, skill } from '../cards';
import type { DemoProps } from '../copy';
export default function BoardDemo({ lang }: DemoProps) {
  const terms = gameTerms[lang];
  const obs = {
    emptyLabel: terms.emptyBoard,
    missingLabel: terms.missingBoard
  };
  const cards = ['Small', 'Medium', 'Large'].map((size, i) =>
    Object.assign(
      card(
        lang,
        pinned.find((p) => p.size === size)!
      ),
      { slot: [0, 2, 5][i], onOpen: () => {} }
    )
  );
  const skills = [
    ...pinnedSkills.map((s) =>
      Object.assign(skill(lang, s), { onOpen: () => {} })
    ),
    Object.assign(skill(lang, pinnedSkills[0]), { id: 'no-art', art: null })
  ];
  return (
    <div className="grid gap-4">
      <Board cards={cards} {...obs} />
      <Board cards={[]} {...obs} />
      <Board cards={null} {...obs} />
      <SkillStrip
        cards={skills}
        emptyLabel={terms.emptySkills}
        missingLabel={terms.missingSkills}
        label={terms.skills}
      />
      <SkillStrip
        cards={[]}
        emptyLabel={terms.emptySkills}
        missingLabel={terms.missingSkills}
        label={terms.skills}
      />
    </div>
  );
}
