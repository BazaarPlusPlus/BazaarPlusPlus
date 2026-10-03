import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import GlobalShell from './layouts/GlobalShell';
import About from './pages/About';
import History from './pages/History';
import Install from './pages/Install';
import RunDetail from './pages/RunDetail';
import Stream from './pages/Stream';

/** The route tree without a router, so it also renders under a MemoryRouter. */
export function AppRoutes() {
  return (
    <Routes>
      <Route element={<GlobalShell />}>
        <Route path="/" element={<Install />} />
        <Route path="/history" element={<History />} />
        <Route path="/history/:runId" element={<RunDetail />} />
        <Route path="/stream" element={<Stream />} />
        <Route path="/about" element={<About />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}

export default function App() {
  return (
    <BrowserRouter>
      <AppRoutes />
    </BrowserRouter>
  );
}
