<script lang="ts">
  import AppModal from '$lib/components/AppModal.svelte';
  import { locale } from '$lib/locale';

  export let open: boolean;
  export let installAcknowledged: boolean;
  export let confirming = false;
  export let bilibiliUrl: string;
  export let onOpenBilibili: (event?: MouseEvent) => void;
  export let onConfirm: () => void | Promise<void>;
</script>

<AppModal
  {open}
  eyebrow="BazaarPlusPlus"
  title={$locale === 'zh' ? '安装 BazaarPlusPlus' : 'Install BazaarPlusPlus'}
  bodyClass="install-preview"
  confirmText={$locale === 'zh' ? '确认安装' : 'Install'}
  confirmBusy={confirming}
  confirmBusyText={$locale === 'zh' ? '检查中...' : 'Checking...'}
  confirmDisabled={!installAcknowledged || confirming}
  wide={true}
  {onConfirm}
>
  <section class="install-impact">
    <div class="install-impact-copy">
      <p class="install-impact-kicker">
        {$locale === 'zh' ? '使用教程' : 'How to Use It'}
      </p>
      <p class="install-impact-body">
        {$locale === 'zh'
          ? '查看 B 站 BazaarPlusPlus 最新视频获取使用教程。'
          : 'Check the latest BazaarPlusPlus video on Bilibili for the usage tutorial.'}
      </p>
    </div>

    <div class="install-impact-action">
      <a
        class="install-impact-link"
        href={bilibiliUrl}
        rel="noreferrer"
        target="_blank"
        onclick={onOpenBilibili}
      >
        {$locale === 'zh' ? '查看最新视频' : 'Watch Latest Video'}
      </a>
    </div>
  </section>

  <label class="install-acknowledge">
    <input
      class="install-acknowledge-input"
      bind:checked={installAcknowledged}
      type="checkbox"
    />
    <span class="install-acknowledge-box" aria-hidden="true"></span>
    <span>
      {$locale === 'zh'
        ? '我确认安装插件存在风险，并愿意自行承担相关责任'
        : 'I understand that installing this plugin involves risk, and I accept responsibility for proceeding.'}
    </span>
  </label>
</AppModal>

<style>
  .install-impact {
    display: grid;
    grid-template-columns: minmax(0, 1fr) auto;
    gap: 1rem;
    align-items: center;
    padding: 0.92rem 1rem;
    text-align: left;
    border: 1px solid rgba(200, 148, 55, 0.14);
    border-radius: 4px;
    background:
      linear-gradient(
        180deg,
        rgba(200, 148, 55, 0.04),
        rgba(200, 148, 55, 0.015)
      ),
      rgba(12, 8, 4, 0.78);
    box-shadow: inset 0 0 0 1px rgba(255, 198, 98, 0.03);
  }

  .install-impact-copy {
    display: grid;
    gap: 0.42rem;
    min-width: 0;
  }

  .install-impact-kicker {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.6rem;
    letter-spacing: 0.18em;
    text-transform: uppercase;
    color: rgba(216, 188, 123, 0.8);
  }

  .install-impact-body {
    margin: 0;
    font-size: 0.82rem;
    line-height: 1.55;
    color: rgba(200, 170, 120, 0.7);
  }

  .install-impact-action {
    justify-self: end;
  }

  .install-impact-link {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: fit-content;
    margin-top: 0.15rem;
    padding: 0.58rem 0.82rem;
    border: 1px solid rgba(214, 169, 84, 0.24);
    border-radius: 3px;
    background: linear-gradient(
      180deg,
      rgba(200, 148, 55, 0.12),
      rgba(200, 148, 55, 0.06)
    );
    color: rgba(236, 225, 202, 0.88);
    text-decoration: none;
    font-family: 'Cinzel', serif;
    font-size: 0.64rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    transition:
      background 0.15s ease,
      border-color 0.15s ease,
      transform 0.15s ease;
  }

  .install-impact-link:hover {
    background: linear-gradient(
      180deg,
      rgba(200, 148, 55, 0.2),
      rgba(200, 148, 55, 0.1)
    );
    border-color: rgba(200, 148, 55, 0.4);
    transform: translateY(-1px);
  }

  .install-impact-link:focus-visible {
    outline: 2px solid rgba(255, 214, 140, 0.9);
    outline-offset: 2px;
  }

  .install-acknowledge {
    display: grid;
    grid-template-columns: auto auto 1fr;
    gap: 0.7rem;
    align-items: start;
    padding: 0.8rem 0.88rem;
    border: 1px solid rgba(200, 148, 55, 0.18);
    border-radius: 4px;
    background:
      linear-gradient(
        180deg,
        rgba(200, 148, 55, 0.055),
        rgba(200, 148, 55, 0.015)
      ),
      rgba(12, 8, 4, 0.78);
    box-shadow: inset 0 0 0 1px rgba(255, 198, 98, 0.04);
    text-align: left;
    color: rgba(228, 216, 191, 0.78);
    font-size: 0.8rem;
    line-height: 1.45;
    cursor: pointer;
  }

  .install-acknowledge-input {
    position: absolute;
    opacity: 0;
    pointer-events: none;
  }

  .install-acknowledge-box {
    width: 1.15rem;
    height: 1.15rem;
    margin-top: 0.08rem;
    border: 1px solid rgba(244, 227, 188, 0.58);
    border-radius: 0.28rem;
    background: linear-gradient(
      180deg,
      rgba(255, 255, 255, 0.09),
      rgba(255, 255, 255, 0.03)
    );
    box-shadow:
      0 0 0 1px rgba(255, 198, 98, 0.05) inset,
      0 2px 10px rgba(0, 0, 0, 0.16);
    position: relative;
    transition:
      border-color 0.15s ease,
      background 0.15s ease,
      box-shadow 0.15s ease,
      transform 0.15s ease;
  }

  .install-acknowledge-box::after {
    content: '';
    position: absolute;
    left: 0.33rem;
    top: 0.14rem;
    width: 0.32rem;
    height: 0.62rem;
    border-right: 2px solid transparent;
    border-bottom: 2px solid transparent;
    transform: rotate(45deg);
    transition: border-color 0.15s ease;
  }

  .install-acknowledge-input:checked + .install-acknowledge-box {
    border-color: rgba(240, 201, 120, 0.62);
    background: linear-gradient(
      180deg,
      rgba(212, 160, 64, 0.28),
      rgba(158, 92, 30, 0.22)
    );
    box-shadow:
      0 0 0 1px rgba(255, 198, 98, 0.12) inset,
      0 4px 14px rgba(170, 100, 25, 0.24);
  }

  .install-acknowledge-input:checked + .install-acknowledge-box::after {
    border-color: #fff2ca;
  }

  .install-acknowledge:hover .install-acknowledge-box {
    border-color: rgba(255, 214, 140, 0.8);
    transform: translateY(-1px);
  }

  .install-acknowledge-input:focus-visible + .install-acknowledge-box {
    outline: 2px solid rgba(255, 214, 140, 0.9);
    outline-offset: 2px;
  }

  @media (max-width: 520px) {
    .install-impact,
    .install-acknowledge {
      padding-left: 0.85rem;
      padding-right: 0.85rem;
    }

    .install-impact {
      grid-template-columns: 1fr;
      align-items: start;
    }

    .install-impact-action {
      justify-self: start;
    }
  }
</style>
