import { useEffect, useState, type ComponentType } from 'react';
import { createRoot } from 'react-dom/client';
import { Button } from '@bpp/ui';
import { pages } from './pages';
import { translate, type Locale, type DemoProps } from './copy';
import Foundations from './Foundations';
import mark from '../../design/assets/Logos/bpp-mark.svg';
import './styles/global.css';
const demos = import.meta.glob<{ default: ComponentType<DemoProps> }>(
  './demos/*.tsx',
  { eager: true }
);
const sources = import.meta.glob<string>('./demos/*.tsx', {
  eager: true,
  query: '?raw',
  import: 'default'
});
const slug = location.pathname.replace(/^\/+|\/+$/g, '') || 'overview';
function App() {
  const [lang, setLang] = useState<Locale>(
    new URLSearchParams(location.search).get('lang') === 'en' ? 'en' : 'zh'
  );
  const t = (zh: string, en: string) => translate(lang, zh, en);
  const [menu, setMenu] = useState(false);
  const page = pages.find(([key]) => key === slug);
  const name = page ? t(page[1], page[2]) : t('页面不存在', 'Page not found');
  useEffect(() => {
    document.title = name + ' · BazaarPlusPlus';
    document.documentElement.lang = lang === 'zh' ? 'zh-CN' : 'en';
  }, [name, lang]);
  const href = (key: string) =>
    (key === 'overview' ? '/' : '/' + key) + (lang === 'en' ? '?lang=en' : '');
  const path = './demos/' + slug + '.tsx',
    Demo = demos[path]?.default;
  const nav = (entries: readonly (typeof pages)[number][]) =>
    entries.map(([key, zh, en]) => (
      <a
        className="nav-link"
        aria-current={key === slug ? 'page' : undefined}
        key={key}
        href={href(key)}
      >
        {t(zh, en)}
      </a>
    ));
  return (
    <>
      <header className="flex items-center gap-3 border-b border-line bg-sidebar px-6 py-3">
        <a
          className="flex items-center gap-2 font-semibold"
          href={href('overview')}
        >
          <img src={mark} width="28" height="28" alt="" />
          BazaarPlusPlus
        </a>
        <span className="ml-auto md:hidden">
          <Button size="sm" onClick={() => setMenu(!menu)} aria-expanded={menu}>
            {t('目录', 'Contents')}
          </Button>
        </span>
        <span className="md:ml-auto">
          <Button
            variant="ghost"
            size="sm"
            onClick={() => {
              const next = lang === 'zh' ? 'en' : 'zh';
              history.replaceState(
                null,
                '',
                location.pathname + (next === 'en' ? '?lang=en' : '')
              );
              setLang(next);
            }}
          >
            {t('English', '中文')}
          </Button>
        </span>
      </header>
      <div className="layout">
        <nav
          aria-label={t('组件目录', 'Component directory')}
          className={
            (menu ? 'grid' : 'hidden') +
            ' md:grid content-start border-line md:border-r p-4 bg-sidebar'
          }
        >
          {nav(pages)}
        </nav>
        <main className="min-w-0 p-4 md:p-8 grid content-start gap-6">
          <h1 className="text-display font-semibold">{name}</h1>
          {slug === 'overview' ? (
            <>
              {[
                [t('基础', 'Foundations'), pages.slice(1, 2)],
                [t('界面组件', 'Interface components'), pages.slice(2, 10)],
                [t('游戏组件', 'Game components'), pages.slice(10)]
              ].map(([label, entries]) => (
                <section key={String(label)}>
                  <h2 className="text-title mb-2">{String(label)}</h2>
                  <div className="index-grid">
                    {nav(entries as (typeof pages)[number][])}
                  </div>
                </section>
              ))}
              <a
                className="text-text-2 underline"
                href={
                  'https://github.com/BazaarPlusPlus/BazaarPlusPlus/blob/master/' +
                  t('docs/design.md', 'design/README.md')
                }
              >
                {t('视觉规范', 'Design guidelines')}
              </a>
              {lang === 'en' && (
                <div className="flex gap-4 text-text-2 underline">
                  <a href="https://github.com/BazaarPlusPlus/BazaarPlusPlus/blob/master/design/writing.md">
                    Writing guidelines
                  </a>
                  <a href="https://github.com/BazaarPlusPlus/BazaarPlusPlus/tree/master/design/components">
                    Component guidelines
                  </a>
                </div>
              )}
            </>
          ) : slug === 'foundations' ? (
            <Foundations lang={lang} />
          ) : Demo ? (
            <>
              <section className="min-w-0 rounded-panel border border-line bg-panel p-4">
                <Demo lang={lang} />
              </section>
              <details>
                <summary>{t('示例源码', 'Example source')}</summary>
                <pre className="overflow-x-auto p-4 bg-panel rounded-panel text-code">
                  {sources[path]}
                </pre>
              </details>
            </>
          ) : (
            <a href={href('overview')}>{t('返回概览', 'Back to overview')}</a>
          )}
        </main>
      </div>
    </>
  );
}
createRoot(document.getElementById('root')!).render(<App />);
