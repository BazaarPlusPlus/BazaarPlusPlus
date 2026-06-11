import { ExternalLink } from 'lucide-react';
import { PageHeader } from '../components/ui/PageHeader';
import { useAppBootstrap } from '../features/about/AppBootstrapProvider';
import { useI18n } from '../i18n/LocaleProvider';

export default function About() {
  const { bootstrap } = useAppBootstrap();
  const { t } = useI18n();

  return (
    <div className="flex flex-col gap-6 w-full h-full pb-12 max-w-5xl mx-auto">
      <PageHeader eyebrow="About" title={t('aboutTitle')} />

      <div className="flex flex-col gap-6 flex-1 min-h-0 w-full">
        <section className="p-5 bg-[rgba(18,11,5,0.88)] border border-[rgba(180,130,48,0.13)] rounded-sm shadow-[0_6px_28px_rgba(0,0,0,0.35)] flex flex-col gap-4">
          <div className="flex items-center justify-between">
            <div className="flex flex-col gap-2">
              <h3 className="cinzel font-bold text-lg text-[#e8c87a] m-0">
                BazaarPlusPlus
              </h3>
              <div className="flex items-center gap-3 selectable">
                <span className="cinzel text-[10px] tracking-widest text-[rgba(200,170,120,0.8)] uppercase">
                  {t('aboutAppLabel')}
                </span>
                <span className="px-2 py-0.5 bg-[rgba(80,180,120,0.15)] text-[#6dd9a0] border border-[rgba(80,180,120,0.25)] rounded-sm text-[10px] fira-code">
                  v{bootstrap.app_version}
                </span>
                <div className="w-px h-3 bg-gradient-to-b from-transparent via-[rgba(200,170,120,0.45)] to-transparent" />
                <span className="cinzel text-[10px] tracking-widest text-[rgba(200,170,120,0.8)] uppercase">
                  {t('aboutBppLabel')}
                </span>
                <span className="text-[10px] text-[rgba(200,170,120,0.8)] fira-code">
                  {bootstrap.bundled_bpp_version ?? '-'}
                </span>
              </div>
            </div>
            <a
              href={bootstrap.links.github}
              target="_blank"
              rel="noreferrer"
              className="flex items-center gap-2 px-3 py-2 border border-[rgba(214,169,84,0.24)] rounded-[3px] bg-gradient-to-b from-[rgba(200,148,55,0.12)] to-[rgba(200,148,55,0.06)] text-[rgba(236,225,202,0.88)] cinzel text-[10px] tracking-[0.12em] uppercase no-underline hover:border-[rgba(200,148,55,0.4)]"
            >
              GitHub
              <ExternalLink size={12} />
            </a>
          </div>

          <div className="h-px bg-gradient-to-r from-transparent via-[rgba(200,148,55,0.3)] to-transparent my-2" />

          <div className="flex flex-col gap-3">
            <h4 className="cinzel text-[10px] tracking-widest text-[rgba(200,148,55,0.75)] uppercase m-0">
              {t('aboutCredits')}
            </h4>
            <ul className="flex flex-col gap-1 m-0 p-0 list-none">
              {bootstrap.credits.map((credit) => (
                <ListItem
                  key={`${credit.name}:${credit.role}`}
                  name={credit.name}
                  role={credit.role}
                />
              ))}
            </ul>
          </div>
        </section>

        <section className="p-5 bg-[rgba(18,11,5,0.88)] border border-[rgba(180,130,48,0.13)] rounded-sm shadow-[0_6px_28px_rgba(0,0,0,0.35)] flex flex-col gap-4">
          <h3 className="cinzel text-xs tracking-widest text-[rgba(220,195,145,0.8)] uppercase m-0">
            {t('aboutLicenses')}
          </h3>
          <ul className="flex flex-col gap-1 m-0 p-0 list-none">
            {bootstrap.licenses.map((license) => (
              <ListItem
                key={`${license.name}:${license.category}`}
                name={license.name}
                role={license.license}
                isLicense
              />
            ))}
          </ul>
        </section>
      </div>
    </div>
  );
}

function ListItem({
  name,
  role,
  isLicense = false
}: {
  name: string;
  role?: string;
  isLicense?: boolean;
}) {
  return (
    <li className="flex items-center justify-between px-3 py-2 bg-[rgba(200,148,55,0.04)] border border-[rgba(180,130,48,0.08)] rounded-sm hover:bg-[rgba(200,148,55,0.08)] transition-colors">
      <span className="fira-code text-xs text-[rgba(228,216,191,0.85)]">
        {name}
      </span>
      {role && (
        <span
          className={`${isLicense ? 'fira-code text-[10px]' : 'cinzel text-[10px] tracking-widest uppercase'} text-[rgba(200,170,120,0.8)]`}
        >
          {role}
        </span>
      )}
    </li>
  );
}
