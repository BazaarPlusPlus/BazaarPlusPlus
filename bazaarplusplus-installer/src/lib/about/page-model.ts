import type { Locale } from '../i18n.ts';

export interface AboutPageModel {
  localeBadge: string;
  localeButtonLabel: string;
  paymentModalTitle: string;
  paymentModalCloseLabel: string;
  paymentCardTitle: string;
  paymentCardBody: string;
  paymentSupportNote: string;
  paymentSupportTip: string;
  supporterEntryTitle: string;
  supporterEntrySubtitle: string;
  paymentActionLabel: string;
  paymentActionHint: string;
}

export function createAboutPageModel(locale: Locale): AboutPageModel {
  const isZh = locale === 'zh';

  return {
    localeBadge: isZh ? '中' : 'EN',
    localeButtonLabel: isZh ? 'Switch to English' : '切换到中文',
    paymentModalTitle: isZh ? '支持项目' : 'Support the Project',
    paymentModalCloseLabel: isZh ? '关闭' : 'Close',
    paymentCardTitle: isZh ? '支持项目' : 'Support the Project',
    paymentCardBody: isZh ? '请 Bazaar++ 喝一杯' : 'Buy Bazaar++ a drink.',
    paymentSupportNote: isZh
      ? '有你支持，Bazaar++ 会冒出更多好东西'
      : 'With your support, Bazaar++ gets to grow more good stuff.',
    paymentSupportTip: isZh
      ? '如果愿意，欢迎在备注里留一个支持者 ID'
      : 'If you want, you can leave a supporter ID in the payment note.',
    supporterEntryTitle: isZh ? '支持者名单' : 'Supporters',
    supporterEntrySubtitle: isZh ? '查看名单' : 'Open list',
    paymentActionLabel: isZh ? '微信' : 'WePay',
    paymentActionHint: isZh ? '感谢支持' : 'Support'
  };
}
