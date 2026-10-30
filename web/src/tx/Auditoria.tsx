import { get, fechaHora } from '../api';
import { Aviso, Tabla, useDatos } from '../comp';

export default function Auditoria() {
  const { datos, error } = useDatos(() => get('/api/auditoria'), []);
  return (
    <div className="pantalla">
      <h2>Auditoría de cambios <code>ZAUD</code></h2>
      {error && <Aviso tipo="error">{error}</Aviso>}
      <Tabla filas={datos ?? []} clave={(a: any) => String(a.id)}
        columnas={[
          { id: 'hora', titulo: 'Fecha', render: (a: any) => fechaHora(a.hora) }, { id: 'usuario', titulo: 'Usuario' },
          { id: 'tabla', titulo: 'Qué' }, { id: 'clave', titulo: 'Clave', mono: true }, { id: 'cambio', titulo: 'Cambio', mono: true, valor: (a: any) => JSON.stringify(a.cambio) },
        ]} />
    </div>
  );
}
