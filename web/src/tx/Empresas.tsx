import { useState } from 'react';
import { get, put, sesion } from '../api';
import { Aviso, Campo, Modal, Tabla, useDatos, useMensaje } from '../comp';

export default function Empresas() {
  const { datos, error, recargar } = useDatos(() => get('/api/empresas'), []);
  const [editar, setEditar] = useState<any>(null);
  const [texto, setTexto] = useState('');
  const mostrar = useMensaje();
  const filas = (datos ?? []).filter((e: any) => !texto || `${e.codigo} ${e.nombre} ${e.alias.join(' ')}`.toUpperCase().includes(texto.toUpperCase()));
  return (
    <div className="pantalla">
      <h2>Empresas <code>ZEMP</code></h2>
      <Aviso tipo="info">La empresa nunca se elige a mano: sale del cliente que informa el buscador para el contenedor o artículo. Los alias sirven para reconocer otros nombres (ej. Notro Bazar = Notro Bazar).</Aviso>
      <div className="acciones"><input placeholder="Buscar…" value={texto} onChange={(e) => setTexto(e.target.value)} style={{ border: '1px solid var(--borde)', borderRadius: 3, height: 30, padding: '0 8px', width: 260 }} /></div>
      {error && <Aviso tipo="error">{error}</Aviso>}
      <Tabla filas={filas} clave={(e: any) => String(e.codigo)} alDobleClic={(e: any) => sesion().rol === 'admin' && setEditar(e)}
        columnas={[{ id: 'codigo', titulo: 'Código WMS', num: true }, { id: 'nombre', titulo: 'Nombre' }, { id: 'alias', titulo: 'Alias', valor: (e: any) => e.alias.join(', ') }]} />
      {editar && (
        <Modal titulo={`Empresa ${editar.codigo}`} cerrar={() => setEditar(null)}
          pie={<><button className="boton" onClick={() => setEditar(null)}>Cancelar</button><button className="boton primario" onClick={async () => {
            await put(`/api/empresas/${editar.codigo}`, { nombre: editar.nombre, alias: String(editar.aliasTxt ?? editar.alias.join(', ')).split(',').map((s: string) => s.trim()).filter(Boolean) });
            mostrar('Empresa guardada', 'ok'); setEditar(null); recargar();
          }}>Guardar</button></>}>
          <Campo etiqueta="Nombre"><input value={editar.nombre} onChange={(e) => setEditar({ ...editar, nombre: e.target.value })} /></Campo>
          <Campo etiqueta="Alias (separados por coma)"><input value={editar.aliasTxt ?? editar.alias.join(', ')} onChange={(e) => setEditar({ ...editar, aliasTxt: e.target.value })} /></Campo>
        </Modal>
      )}
    </div>
  );
}
