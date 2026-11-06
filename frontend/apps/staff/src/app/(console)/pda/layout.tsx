// Los estilos del panel de avisos viven en components/avisos.css (scope `.avisos-root`).
export default function PdaLayout({ children }: { children: React.ReactNode }) {
  return <div className="pda-root h-full">{children}</div>;
}
