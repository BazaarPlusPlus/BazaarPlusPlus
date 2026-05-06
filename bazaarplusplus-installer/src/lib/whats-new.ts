export type LocalizedText = {
  zh: string;
  en: string;
};

export type HighlightTone = 'default' | 'featured' | 'warning';

export type HighlightSection = {
  icon: string;
  sectionTitle?: LocalizedText;
  sectionSummary?: LocalizedText;
  title: LocalizedText;
  bullets: LocalizedText[];
  tone?: HighlightTone;
  badge?: LocalizedText;
  actionLead?: LocalizedText;
  actionLabel?: LocalizedText;
};

export type WhatsNewRelease = {
  version: string;
  kicker: LocalizedText;
  releaseLabel: LocalizedText;
  summary: LocalizedText;
  sections: HighlightSection[];
};

export const whatsNewReleases: WhatsNewRelease[] = [
  {
    version: '2.3.7',
    kicker: {
      zh: '当前版本 · 更新亮点',
      en: "Current Build · What's New"
    },
    releaseLabel: {
      zh: '更新概览',
      en: 'Release Overview'
    },
    summary: {
      zh: '本次更新继续打磨「镜像战斗回放」体验，带来一轮针对性优化；同时，战斗历史面板现已支持中文，并完成了新一轮视觉升级。',
      en: 'This release continues polishing Ghost Battle Replay, adds a focused round of improvements, and brings Chinese support plus a visual refresh to the battle history panel.'
    },
    sections: [
      {
        icon: 'I',
        sectionTitle: {
          zh: '主打功能',
          en: 'Featured'
        },
        title: {
          zh: '镜像战斗回放',
          en: 'Ghost Battle Replay'
        },
        bullets: [
          {
            zh: '你的构筑，不止属于你这一局',
            en: 'Your build no longer belongs to just one run.'
          }
        ],
        tone: 'featured',
        badge: {
          zh: '主打功能',
          en: 'Featured Update'
        },
        actionLead: {
          zh: '太牛了',
          en: 'Love It'
        },
        actionLabel: {
          zh: '支持作者',
          en: 'Support Author'
        }
      },
      {
        icon: 'II',
        sectionTitle: {
          zh: '新增功能',
          en: 'New Feature'
        },
        sectionSummary: {
          zh: '当战绩记录需要清理，或历史面板出现异常时，可以用它快速恢复。',
          en: 'A new recovery tool for clearing space or fixing cases where the history panel could not be opened.'
        },
        title: {
          zh: '重置战绩',
          en: 'Reset History'
        },
        badge: {
          zh: '新增功能',
          en: 'New Feature'
        },
        bullets: [
          {
            zh: '如果觉得记录太占空间，或者之前遇到打不开的情况，现在都可以用这个功能进行重置或恢复。',
            en: 'Added a reset history feature that can be used to clear space or recover when the panel could not be opened before.'
          }
        ]
      },
      {
        icon: 'III',
        sectionTitle: {
          zh: '界面更新',
          en: 'Panel Refresh'
        },
        sectionSummary: {
          zh: '历史战绩面板现在不只更易读，也更贴近中文玩家的使用习惯。',
          en: 'The battle history panel is now easier to read and better suited for Chinese-speaking players.'
        },
        title: {
          zh: '历史战绩面板支持中文并优化视觉表现',
          en: 'Battle History Panel Chinese Support and Visual Refresh'
        },
        bullets: [
          {
            zh: '历史战绩面板现已支持中文显示。',
            en: 'The battle history panel now supports Chinese.'
          },
          {
            zh: '同时对面板视觉表现做了一轮整理和优化。',
            en: 'The panel visuals have also been cleaned up and improved.'
          },
          {
            zh: '回归了通过 F8 开关历史战绩面板的功能。',
            en: 'Restored the ability to toggle the battle history panel with F8.'
          }
        ]
      },
      {
        icon: 'IV',
        sectionTitle: {
          zh: '功能优化',
          en: 'Replay Improvements'
        },
        sectionSummary: {
          zh: '围绕镜像战斗回放本身补了一轮优化，让整体体验更顺手。',
          en: 'A focused polish pass on Ghost Battle Replay to make the feature smoother to use.'
        },
        title: {
          zh: '镜像战斗回放优化',
          en: 'Ghost Battle Replay Improvements'
        },
        bullets: [
          {
            zh: '对镜像战斗回放进行了进一步优化，整体体验更加顺畅稳定。',
            en: 'Ghost Battle Replay has been refined further for a smoother and more stable overall experience.'
          }
        ]
      }
    ]
  },
  {
    version: '2.3.6',
    kicker: {
      zh: '当前版本 · 更新亮点',
      en: "Current Build · What's New"
    },
    releaseLabel: {
      zh: '更新概览',
      en: 'Release Overview'
    },
    summary: {
      zh: '本次更新以「镜像战斗回放」为核心，补齐相关数据能力。同时对 History Panel、操作绑定及信息展示进行了整体优化与整理。',
      en: 'This release centers on Mirror Battle Replay and the data support behind it, with the rest of the update focused on polishing History Panel, control bindings, and information display.'
    },
    sections: [
      {
        icon: 'I',
        sectionTitle: {
          zh: '主打功能',
          en: 'Featured'
        },
        title: {
          zh: '镜像战斗回放',
          en: 'Ghost Battle Replay'
        },
        bullets: [
          {
            zh: '你的构筑，不止属于你这一局',
            en: 'Your build no longer belongs to just one run.'
          }
        ],
        tone: 'featured',
        badge: {
          zh: '主打功能',
          en: 'Featured Update'
        },
        actionLead: {
          zh: '太牛了',
          en: 'Love It'
        },
        actionLabel: {
          zh: '支持作者',
          en: 'Support Author'
        }
      },
      {
        icon: 'II',
        sectionTitle: {
          zh: '体验优化',
          en: 'Experience Improvements'
        },
        sectionSummary: {
          zh: '围绕回放体验与日常操作，进行了一轮稳定性与性能优化',
          en: 'A full pass on replay flow and everyday use, with better stability and lower overhead.'
        },
        title: {
          zh: '优化 History Panel',
          en: 'History Panel Improvements'
        },
        bullets: [
          {
            zh: '打开时再加载数据，减少常驻负担',
            en: 'History now loads on demand to reduce background overhead.'
          },
          {
            zh: '支持删除 run，并优化数据采集时机',
            en: 'Runs can now be deleted, and data capture timing has been tuned.'
          }
        ]
      },
      {
        icon: 'III',
        sectionTitle: {
          zh: '操作优化',
          en: 'Controls'
        },
        title: {
          zh: '快捷键支持鼠标绑定',
          en: 'Mouse Button Hotkeys'
        },
        bullets: [
          {
            zh: '快捷键现在支持绑定到鼠标按键',
            en: 'Hotkeys can now be assigned to mouse buttons.'
          }
        ]
      },
      {
        icon: 'IV',
        sectionTitle: {
          zh: '信息展示优化',
          en: 'Display Improvements'
        },
        title: {
          zh: 'Tooltip 预览升级',
          en: 'Tooltip Preview Upgrade'
        },
        bullets: [
          {
            zh: '合并原有双 Tooltip，信息展示更加集中清晰',
            en: 'The previous dual-tooltip layout has been merged into a single, clearer view.'
          }
        ]
      },
      {
        icon: 'V',
        sectionTitle: {
          zh: '内容补充',
          en: 'Content'
        },
        title: {
          zh: '随机英雄禁用',
          en: 'Random Hero Restrictions'
        },
        bullets: [
          {
            zh: '新增随机英雄禁用部分英雄的选项',
            en: 'Added an option to exclude specific heroes from random hero selection.'
          }
        ]
      }
    ]
  }
];

export function resolveWhatsNewRelease(
  version: string | null
): WhatsNewRelease {
  const normalizedVersion = version?.trim() ?? '';
  return (
    whatsNewReleases.find((release) => release.version === normalizedVersion) ??
    whatsNewReleases[0]
  );
}
