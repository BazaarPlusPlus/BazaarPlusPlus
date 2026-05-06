export interface LocalizedLabel {
  zh: string;
  en: string;
}

export interface PaymentMethod {
  id: string;
  name: LocalizedLabel;
  src: string;
  accent: string;
}

export interface ExternalLink {
  name: string;
  url: string;
}

export interface LicensedDependency {
  name: string;
  license: string;
  url: string;
}

export interface AboutAuthor {
  name: string;
  url: string;
  roleKey: 'aboutAuthorRole' | 'aboutCocreatorRole';
}

export const paymentMethods: PaymentMethod[] = [
  {
    id: 'wechat',
    name: {
      zh: '微信收款码',
      en: 'WePay'
    },
    src: '/support/wechat-pay.svg',
    accent: 'payment-card-wechat'
  }
];

export const authors: AboutAuthor[] = [
  {
    name: 'cauyxy',
    url: 'https://github.com/cauyxy',
    roleKey: 'aboutAuthorRole'
  },
  {
    name: 'Codex',
    url: 'https://openai.com/codex',
    roleKey: 'aboutCocreatorRole'
  },
  {
    name: 'Claude Code',
    url: 'https://claude.com/product/claude-code',
    roleKey: 'aboutCocreatorRole'
  }
];

export const supportLinks: ExternalLink[] = [
  {
    name: 'Ko-fi',
    url: 'https://ko-fi.com/cauyxy'
  }
];

export const inspiredBy: ExternalLink[] = [
  {
    name: 'BazaarHelper',
    url: 'https://github.com/Duangi/BazaarHelper'
  },
  {
    name: 'BazaarPlannerMod',
    url: 'https://github.com/oceanseth/BazaarPlannerMod'
  }
];

export const dataSources: ExternalLink[] = [
  {
    name: 'BazaarDB',
    url: 'https://bazaardb.gg'
  }
];

export const projectDependencies: LicensedDependency[] = [
  {
    name: 'BepInEx',
    license: 'LGPL-2.1',
    url: 'https://github.com/BepInEx/BepInEx'
  }
];

export const frontendDependencies: LicensedDependency[] = [
  {
    name: 'Svelte',
    license: 'MIT',
    url: 'https://svelte.dev'
  },
  {
    name: 'SvelteKit',
    license: 'MIT',
    url: 'https://kit.svelte.dev'
  },
  {
    name: 'Vite',
    license: 'MIT',
    url: 'https://vitejs.dev'
  },
  {
    name: 'Tauri',
    license: 'MIT / Apache-2.0',
    url: 'https://tauri.app'
  }
];

export const rustDependencies: LicensedDependency[] = [
  {
    name: 'serde',
    license: 'MIT / Apache-2.0',
    url: 'https://serde.rs'
  },
  {
    name: 'reqwest',
    license: 'MIT / Apache-2.0',
    url: 'https://github.com/seanmonstar/reqwest'
  },
  {
    name: 'zip',
    license: 'MIT',
    url: 'https://github.com/zip-rs/zip2'
  },
  {
    name: 'dirs',
    license: 'MIT / Apache-2.0',
    url: 'https://github.com/dirs-dev/dirs-rs'
  },
  {
    name: 'keyvalues-parser',
    license: 'MIT',
    url: 'https://github.com/CosmicHorrorDev/keyvalues-rs'
  },
  {
    name: 'winreg',
    license: 'MIT',
    url: 'https://github.com/gentoo90/winreg-rs'
  }
];
