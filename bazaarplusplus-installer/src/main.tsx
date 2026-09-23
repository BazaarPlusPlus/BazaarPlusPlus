import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import { scheduleNotoSansScLoad } from './features/shared/notoSansSc';
import { isWindowsPlatform } from './features/shared/platform';
import { hasTauriRuntime } from './api/runtime';
import { LocaleProvider } from './i18n/LocaleProvider';
import './styles/index.css';

// Platform-dependent chrome (Windows fonts, the macOS overlay title bar's
// traffic-light inset) must be right on the first frame.
const windows = isWindowsPlatform();
document.documentElement.dataset.bppPlatform = windows ? 'windows' : 'macos';
if (!windows && hasTauriRuntime()) {
  document.documentElement.dataset.bppTitlebar = 'overlay';
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <LocaleProvider>
      <App />
    </LocaleProvider>
  </StrictMode>
);

scheduleNotoSansScLoad();
