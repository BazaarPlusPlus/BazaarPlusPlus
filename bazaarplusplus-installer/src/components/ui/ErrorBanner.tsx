export function ErrorBanner({ message }: { message: string }) {
  return (
    <p
      role="alert"
      className="bpp-error-banner m-0 px-4 py-3 text-sm selectable"
    >
      {message}
    </p>
  );
}
