export function ErrorBanner({ message }: { message: string }) {
  return (
    <p
      role="alert"
      className="m-0 px-4 py-3 border border-[rgba(217,109,109,0.28)] bg-[rgba(217,109,109,0.08)] text-[#d96d6d] text-sm selectable"
    >
      {message}
    </p>
  );
}
