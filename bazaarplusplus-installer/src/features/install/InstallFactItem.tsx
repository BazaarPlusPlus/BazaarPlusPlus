export function InstallFactItem({
  label,
  value
}: {
  label: string;
  value: string;
}) {
  return (
    <li className="flex justify-between text-sm border-b border-[rgba(200,148,55,0.08)] py-2">
      <span className="text-[rgba(200,170,120,0.8)]">{label}</span>
      <span className="fira-code text-[#e8dcc8]">{value}</span>
    </li>
  );
}
