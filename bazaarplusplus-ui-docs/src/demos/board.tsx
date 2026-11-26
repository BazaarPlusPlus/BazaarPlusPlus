import { Board, SkillStrip } from '@bpp/ui';
import { card, pinned, pinnedSkills, skill } from '../cards';
import { type DemoProps, translate } from '../copy';
export default function BoardDemo({ lang }: DemoProps) {
  const obs = {
    emptyLabel: translate(lang, '空阵容', 'Empty board'),
    missingLabel: translate(lang, '未记录阵容', 'Board not recorded')
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
        emptyLabel={translate(lang, '无技能', 'No skills')}
        missingLabel={translate(lang, '未记录技能', 'Skills not recorded')}
        label={translate(lang, '技能', 'Skills')}
      />
      <SkillStrip
        cards={[]}
        emptyLabel={translate(lang, '无技能', 'No skills')}
        missingLabel={translate(lang, '未记录技能', 'Skills not recorded')}
        label={translate(lang, '技能', 'Skills')}
      />
    </div>
  );
}
