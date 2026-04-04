<script lang="ts">
    import AppModal from '$lib/components/AppModal.svelte';
    import { locale } from '$lib/locale';

    type LocalizedText = {
        zh: string;
        en: string;
    };

    type HighlightTone = 'default' | 'featured' | 'warning';

    type HighlightSection = {
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

    let showSupportQr = false;

    const supportQrCopy = {
        zh: {
            title: '支持 BazaarPlusPlus',
            cardTitle: '支持项目',
            cardBody: '请 Bazaar++ 喝一杯',
            note: '有你支持，Bazaar++ 会冒出更多好东西',
            tip: '如果愿意，欢迎在备注里留一个支持者 ID',
            close: '关闭'
        },
        en: {
            title: 'Support BazaarPlusPlus',
            cardTitle: 'Support the Project',
            cardBody: 'Buy Bazaar++ a drink.',
            note: 'With your support, Bazaar++ gets to grow more good stuff.',
            tip: 'If you want, you can leave a supporter ID in the payment note.',
            close: 'Close'
        }
    } as const;

    const sections: HighlightSection[] = [
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
    ];
</script>

<AppModal
    open={showSupportQr}
    eyebrow="BazaarPlusPlus"
    title={$locale === 'zh' ? supportQrCopy.zh.title : supportQrCopy.en.title}
    bodyClass="support-modal-body"
    confirmText={$locale === 'zh'
        ? supportQrCopy.zh.close
        : supportQrCopy.en.close}
    onConfirm={() => {
        showSupportQr = false;
    }}
>
    <section class="support-modal-shell">
        <div class="payment-grid">
            <article class="payment-card payment-card-wechat">
                <div class="payment-frame">
                    <img
                        class="payment-image"
                        src="/support/wechat-pay.svg"
                        alt="WePay"
                    />
                </div>

                <div class="payment-copy">
                    <h3>
                        {$locale === 'zh'
                            ? supportQrCopy.zh.cardTitle
                            : supportQrCopy.en.cardTitle}
                    </h3>
                    <p>
                        {$locale === 'zh'
                            ? supportQrCopy.zh.cardBody
                            : supportQrCopy.en.cardBody}
                    </p>
                </div>
            </article>
        </div>

        <p class="payment-support-note">
            {$locale === 'zh' ? supportQrCopy.zh.note : supportQrCopy.en.note}
        </p>
        <p class="payment-support-tip">
            {$locale === 'zh' ? supportQrCopy.zh.tip : supportQrCopy.en.tip}
        </p>
    </section>
</AppModal>

<section class="update-hero">
    <p class="update-kicker">
        {$locale === 'zh'
            ? '当前版本 · 更新亮点'
            : "Current Build · What's New"}
    </p>
    <h2 class="update-title">BazaarPlusPlus</h2>
    <p class="update-release-label">
        {$locale === 'zh' ? '更新概览' : 'Release Overview'}
    </p>
    <p class="update-summary">
        {$locale === 'zh'
            ? '本次更新以「镜像战斗回放」为核心，补齐相关数据能力。\n同时对 History Panel、操作绑定及信息展示进行了整体优化与整理。'
            : 'This release centers on Mirror Battle Replay and the data support behind it, with the rest of the update focused on polishing History Panel, control bindings, and information display.'}
    </p>
</section>

<div class="update-group-list">
    {#each sections as section}
        <section class="update-group">
            <header class="update-group-header">
                <p class="update-group-kicker">
                    {section.icon}
                    {$locale === 'zh'
                        ? section.sectionTitle?.zh
                        : section.sectionTitle?.en}
                </p>
                {#if section.sectionSummary}
                    <p class="update-group-summary">
                        {$locale === 'zh'
                            ? section.sectionSummary.zh
                            : section.sectionSummary.en}
                    </p>
                {/if}
            </header>

            <article
                class={`update-feature-card tone-${section.tone ?? 'default'}`}
            >
                <div class="update-feature-icon">{section.icon}</div>
                <div class="update-feature-copy">
                    {#if section.badge}
                        <p class="update-feature-badge">
                            {$locale === 'zh'
                                ? section.badge.zh
                                : section.badge.en}
                        </p>
                    {/if}
                    <h3>
                        {$locale === 'zh' ? section.title.zh : section.title.en}
                    </h3>
                    <ul class="update-feature-points">
                        {#each section.bullets as bullet}
                            <li>{$locale === 'zh' ? bullet.zh : bullet.en}</li>
                        {/each}
                    </ul>
                </div>
                {#if section.actionLabel && section.actionLead}
                    <div class="update-feature-action">
                        <button
                            class="featured-support-button"
                            type="button"
                            onclick={() => (showSupportQr = true)}
                        >
                            <span class="featured-support-lead"
                                >{$locale === 'zh'
                                    ? section.actionLead.zh
                                    : section.actionLead.en}</span
                            >
                            <span class="featured-support-label"
                                >{$locale === 'zh'
                                    ? section.actionLabel.zh
                                    : section.actionLabel.en}</span
                            >
                        </button>
                    </div>
                {/if}
            </article>
        </section>
    {/each}
</div>

<style>
    .update-hero {
        display: grid;
        gap: 0.5rem;
        padding: 1rem 1.05rem;
        text-align: left;
        border: 1px solid rgba(200, 148, 55, 0.18);
        border-radius: 4px;
        background:
            radial-gradient(
                circle at top right,
                rgba(232, 200, 122, 0.16),
                transparent 42%
            ),
            linear-gradient(
                180deg,
                rgba(200, 148, 55, 0.08),
                rgba(200, 148, 55, 0.02)
            ),
            rgba(12, 8, 4, 0.84);
        box-shadow: inset 0 0 0 1px rgba(255, 198, 98, 0.04);
    }

    .update-kicker {
        margin: 0;
        font-family: 'Cinzel', serif;
        font-size: 0.58rem;
        letter-spacing: 0.22em;
        text-transform: uppercase;
        color: rgba(232, 200, 122, 0.7);
    }

    .update-title {
        margin: 0;
        font-family: 'Cinzel', serif;
        font-size: clamp(1rem, 2.6vw, 1.4rem);
        letter-spacing: 0.06em;
        color: rgba(239, 223, 188, 0.95);
    }

    .update-summary {
        margin: 0;
        white-space: pre-line;
        font-size: 0.9rem;
        line-height: 1.65;
        color: rgba(233, 222, 198, 0.84);
    }

    .update-release-label {
        margin: 0.1rem 0 -0.05rem;
        font-family: 'Cinzel', serif;
        font-size: 0.62rem;
        letter-spacing: 0.16em;
        text-transform: uppercase;
        color: rgba(214, 182, 126, 0.72);
    }

    .update-group-list {
        display: grid;
        gap: 1rem;
        margin-top: 0.15rem;
    }

    .update-group {
        display: grid;
        gap: 0.55rem;
    }

    .update-group-header {
        display: grid;
        gap: 0.18rem;
        padding: 0 0.1rem;
    }

    .update-group-kicker {
        margin: 0;
        font-family: 'Cinzel', serif;
        font-size: 0.6rem;
        letter-spacing: 0.2em;
        text-transform: uppercase;
        color: rgba(232, 200, 122, 0.72);
    }

    .update-group-summary {
        margin: 0;
        font-size: 0.78rem;
        line-height: 1.55;
        color: rgba(199, 178, 140, 0.72);
    }

    .update-feature-list {
        display: grid;
        gap: 0.7rem;
        text-align: left;
    }

    .update-feature-card {
        display: grid;
        grid-template-columns: 2.25rem 1fr;
        gap: 0.8rem;
        align-items: start;
        padding: 0.9rem;
        border-radius: 3px;
        box-shadow: inset 0 0 0 1px rgba(255, 198, 98, 0.04);
    }

    .tone-default {
        border: 1px solid rgba(200, 148, 55, 0.18);
        background:
            linear-gradient(
                180deg,
                rgba(200, 148, 55, 0.08),
                rgba(200, 148, 55, 0.02)
            ),
            rgba(12, 8, 4, 0.82);
    }

    .tone-featured {
        grid-template-columns: 2.25rem minmax(0, 1fr) auto;
        border: 1px solid rgba(226, 181, 82, 0.34);
        background:
            radial-gradient(
                circle at top right,
                rgba(255, 218, 120, 0.16),
                transparent 38%
            ),
            linear-gradient(
                180deg,
                rgba(230, 178, 74, 0.14),
                rgba(200, 148, 55, 0.04)
            ),
            rgba(16, 10, 4, 0.88);
        box-shadow:
            inset 0 0 0 1px rgba(255, 216, 125, 0.08),
            0 10px 28px rgba(0, 0, 0, 0.18);
    }

    .tone-warning {
        border: 1px solid rgba(214, 78, 78, 0.4);
        background:
            radial-gradient(
                circle at top right,
                rgba(214, 78, 78, 0.14),
                transparent 42%
            ),
            linear-gradient(
                180deg,
                rgba(165, 44, 44, 0.16),
                rgba(114, 26, 26, 0.06)
            ),
            rgba(16, 8, 8, 0.88);
        box-shadow: inset 0 0 0 1px rgba(255, 132, 132, 0.05);
    }

    .update-feature-icon {
        width: 2.25rem;
        height: 2.25rem;
        display: grid;
        place-items: center;
        border: 1px solid rgba(214, 169, 84, 0.28);
        border-radius: 999px;
        background: radial-gradient(
            circle at 30% 30%,
            rgba(232, 200, 122, 0.22),
            rgba(158, 92, 30, 0.14)
        );
        color: rgba(232, 200, 122, 0.92);
        font-family: 'Cinzel', serif;
        font-size: 0.66rem;
        letter-spacing: 0.12em;
    }

    .tone-featured .update-feature-icon {
        border-color: rgba(255, 212, 111, 0.42);
        background: radial-gradient(
            circle at 30% 30%,
            rgba(255, 219, 129, 0.34),
            rgba(194, 120, 25, 0.18)
        );
        color: rgba(255, 226, 150, 0.98);
    }

    .tone-warning .update-feature-icon {
        border-color: rgba(223, 110, 110, 0.42);
        background: radial-gradient(
            circle at 30% 30%,
            rgba(224, 112, 112, 0.28),
            rgba(133, 31, 31, 0.16)
        );
        color: rgba(255, 182, 182, 0.94);
    }

    .update-feature-copy h3 {
        margin: 0 0 0.35rem;
        font-family: 'Cinzel', serif;
        font-size: 0.86rem;
        letter-spacing: 0.06em;
        color: rgba(239, 223, 188, 0.92);
    }

    .update-feature-badge {
        display: inline-flex;
        align-items: center;
        margin: 0 0 0.35rem;
        padding: 0.18rem 0.5rem;
        border: 1px solid rgba(255, 216, 124, 0.32);
        border-radius: 999px;
        background: rgba(255, 216, 124, 0.08);
        color: rgba(255, 222, 148, 0.92);
        font-family: 'Cinzel', serif;
        font-size: 0.54rem;
        letter-spacing: 0.16em;
        text-transform: uppercase;
    }

    .tone-warning .update-feature-copy h3 {
        color: rgba(255, 202, 202, 0.95);
    }

    .update-feature-action {
        display: flex;
        align-items: center;
        justify-content: flex-end;
        min-height: 100%;
    }

    .featured-support-button {
        display: grid;
        gap: 0.18rem;
        min-width: 13.5rem;
        padding: 0.9rem 1.1rem;
        border: 1px solid rgba(236, 195, 104, 0.34);
        border-radius: 14px;
        background:
            linear-gradient(
                180deg,
                rgba(255, 221, 146, 0.12),
                rgba(204, 142, 40, 0.08)
            ),
            rgba(28, 18, 8, 0.88);
        color: rgba(248, 230, 185, 0.96);
        text-align: left;
        box-shadow:
            inset 0 0 0 1px rgba(255, 225, 154, 0.05),
            0 10px 24px rgba(0, 0, 0, 0.18);
        transition:
            transform 0.15s ease,
            border-color 0.15s ease,
            background 0.15s ease,
            box-shadow 0.15s ease;
    }

    .featured-support-button:hover {
        transform: translateY(-1px);
        border-color: rgba(255, 214, 118, 0.52);
        background:
            linear-gradient(
                180deg,
                rgba(255, 225, 154, 0.16),
                rgba(214, 152, 48, 0.1)
            ),
            rgba(32, 20, 8, 0.92);
        box-shadow:
            inset 0 0 0 1px rgba(255, 229, 162, 0.06),
            0 14px 28px rgba(0, 0, 0, 0.22);
    }

    .featured-support-lead {
        font-family: 'Cinzel', serif;
        font-size: 0.62rem;
        letter-spacing: 0.18em;
        text-transform: uppercase;
        color: rgba(255, 215, 124, 0.78);
    }

    .featured-support-label {
        font-family: 'Cinzel', serif;
        font-size: 0.92rem;
        letter-spacing: 0.08em;
        color: rgba(255, 235, 190, 0.98);
    }

    :global(.support-modal-body) {
        padding-top: 0.1rem;
    }

    .support-modal-shell {
        display: grid;
        gap: 0.9rem;
        text-align: left;
    }

    .payment-grid {
        display: grid;
        grid-template-columns: minmax(0, 260px);
        justify-content: center;
        gap: 0.8rem;
    }

    .payment-support-note {
        margin: -0.1rem 0 0;
        text-align: center;
        font-size: 0.76rem;
        line-height: 1.6;
        color: rgba(214, 190, 146, 0.76);
    }

    .payment-support-tip {
        margin: -0.2rem auto 0;
        max-width: 28rem;
        text-align: center;
        font-size: 0.72rem;
        line-height: 1.65;
        color: rgba(240, 220, 184, 0.82);
    }

    .payment-card {
        position: relative;
        padding: 0.75rem;
        background:
            radial-gradient(
                circle at top,
                rgba(255, 232, 174, 0.08),
                transparent 54%
            ),
            linear-gradient(180deg, rgba(34, 20, 8, 0.96), rgba(16, 9, 4, 0.98));
        border: 1px solid rgba(200, 148, 55, 0.16);
        border-radius: 4px;
        display: grid;
        gap: 0.65rem;
        box-shadow: inset 0 0 0 1px rgba(255, 198, 98, 0.05);
    }

    .payment-card::after {
        content: '';
        position: absolute;
        inset: 0.45rem;
        border: 1px solid rgba(255, 220, 155, 0.05);
        border-radius: 2px;
        pointer-events: none;
    }

    .payment-card-wechat {
        box-shadow:
            inset 0 0 0 1px rgba(255, 198, 98, 0.05),
            0 10px 32px rgba(42, 110, 78, 0.14);
    }

    .payment-frame {
        aspect-ratio: 1 / 1;
        padding: 0.8rem;
        background: linear-gradient(
            135deg,
            rgba(255, 248, 231, 0.98),
            rgba(245, 238, 220, 0.98)
        );
        border-radius: 3px;
        box-shadow:
            inset 0 0 0 1px rgba(95, 65, 19, 0.08),
            0 10px 24px rgba(0, 0, 0, 0.22);
    }

    .payment-copy {
        display: grid;
        gap: 0.18rem;
        text-align: center;
    }

    .payment-copy h3 {
        margin: 0;
        font-family: 'Cinzel', serif;
        font-size: 0.82rem;
        letter-spacing: 0.04em;
        color: rgba(238, 220, 182, 0.94);
    }

    .payment-copy p {
        margin: 0;
        font-size: 0.66rem;
        line-height: 1.45;
        color: rgba(200, 170, 120, 0.8);
    }

    .payment-image {
        width: 100%;
        height: 100%;
        display: block;
        object-fit: contain;
        background: #fff;
        border-radius: 2px;
    }

    .update-feature-points {
        margin: 0;
        padding-left: 1.1rem;
        display: grid;
        gap: 0.35rem;
        color: rgba(233, 222, 198, 0.82);
        line-height: 1.62;
        font-size: 0.88rem;
    }

    .tone-featured .update-feature-points {
        color: rgba(245, 231, 198, 0.9);
    }

    .tone-warning .update-feature-points {
        color: rgba(244, 214, 214, 0.9);
    }

    .update-feature-points li::marker {
        color: rgba(232, 200, 122, 0.72);
    }

    .tone-warning .update-feature-points li::marker {
        color: rgba(239, 126, 126, 0.88);
    }

    @media (max-width: 560px) {
        .update-feature-card {
            grid-template-columns: 1fr;
        }

        .tone-featured {
            grid-template-columns: 1fr;
        }

        .update-feature-icon {
            width: 2rem;
            height: 2rem;
        }

        .update-feature-action {
            justify-content: stretch;
        }

        .featured-support-button {
            width: 100%;
            min-width: 0;
        }

        .payment-grid {
            grid-template-columns: 1fr;
        }
    }
</style>
