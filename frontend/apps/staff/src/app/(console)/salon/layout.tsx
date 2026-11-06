import "./salon.css";

// Envuelve la vista del mesero en `.salon-root` para scopear sus estilos
// (portados de la antigua app waiter) y no afectar cashdesk/board.
export default function SalonLayout({ children }: { children: React.ReactNode }) {
  return <div className="salon-root">{children}</div>;
}
