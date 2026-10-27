# Sprint 6 – Pool Compartido

Guía del sprint para el equipo: **Compañero 1 = Francisco (Díaz)** y **Compañero 2 = Octavio (Brachetti)**. También está pensada para pasársela a Claude como contexto. Las convenciones generales (excepciones, estilo, ramas) siguen siendo las de `docs/Sprint4.md`, y la estructura del código está en `docs/Sprint5.md`. Este documento agrega lo que cambió desde entonces y las historias nuevas.

---

## 1. Objetivo del sprint

Pulir la experiencia: que ningún aviso se pierda, que la app se vea moderna, que sirva para el día a día de un estudiante (siempre los mismos días y horarios) y que se pueda confiar en con quién viajás. Varias ideas salen de mirar una app parecida (PUL, pul.com.ar).

| HU | Qué es | Quién | Orden |
|---|---|---|---|
| HU26 | Centro de notificaciones (avisos en todas las pantallas + campanita) | Compañero 1 (Francisco) | 1 |
| HU29 | Rediseño de las tarjetas de viaje (avatares, íconos, chips, "Tu próximo viaje") | Compañero 1 (Francisco) | 2 |
| HU30 | Viajes que coinciden con mi horario y mi zona | Compañero 1 (Francisco) | 3 (si da el tiempo) |
| HU28 | Rutina fija: publicar un viaje que se repite toda la semana o el cuatrimestre | Compañero 2 (Octavio) | 1 |
| HU31 | Calificaciones después del viaje | Compañero 2 (Octavio) | 2 |

Backlog futuro: HU9 (cuántas veces viajé con alguien), HU20 (ingreso con Google) y HU27 (registro solo con mail de la facultad; se dejó para más adelante porque limita las pruebas y a usuarios de otras facultades).

---

## 2. Cómo trabajamos

Igual que en el Sprint 5:

1. **Una rama por HU**, sin espacios: `hu-26-fran`, `hu-27-octa`, etc. Se crea desde `main` después de hacer Pull.
2. **Commit + push** seguido. Al terminar: pull request en GitHub y merge.
3. **Después de cada merge**, los dos vuelven a `main` y hacen Pull. Si tenés una rama abierta, combiná `main` en tu rama (Git → Administrar ramas → clic derecho en `main` → **Fusionar** en la rama actual).
4. **Probar con dos usuarios** (una PC cada uno, o PC + celular con el APK) antes del merge.
5. **`Servicios/ClavesFirebase.cs` no está en GitHub.** No borrarlo ni subirlo.
6. **Reglas de Firebase:** se editan en `database.rules.json` (raíz del repo) partiendo de la versión de `main`, y las publica **Francisco** en la consola. Nunca pegar en la consola un archivo armado aparte: siempre el de `main` con los cambios agregados.

### Archivos que tocamos los dos (avisar por WhatsApp antes)

| Archivo | Francisco | Octavio |
|---|---|---|
| `MisViajesPage.xaml(.cs)` | campanita en la barra (HU26) y diseño de la tarjeta (HU29) | "Cancelar solo este / toda la serie" (HU28) y botón "Calificar" (HU31) |
| `MainPage.xaml(.cs)` | se saca el panel de avisos (pasa al servicio de notificaciones, HU26) y tarjetas (HU29) | — |
| `PublicarViajePage.xaml(.cs)` | — | sección "Repetir" (HU28) |
| `Servicios/DatosFirebase.cs` + `ConversorViajes` | — | `SerieId` en `ViajeDatos` (HU28), datos de calificaciones (HU31) |
| `PerfilPage.xaml(.cs)` | campanita en la barra (HU26) | promedio de calificaciones (HU31) |
| `Servicios/ConversorMensajes.cs` + `ViajeEvento` | — | evento nuevo de calificación (HU31) |
| `database.rules.json` | — | `serieId` en `/viajes` (HU28) y nodo `/calificaciones` (HU31) |

**Orden recomendado para no pisarse:** Octavio arranca por la HU28 con la parte de **Publicar** (`PublicarViajePage` + `ServicioViajes`), que no toca nada de Francisco. La parte de `MisViajesPage` ("solo este / toda la serie") y la HU31 conviene hacerlas **después del merge de la HU26**, o tocar solo sus métodos y avisar.

---

## 3. Qué cambió en el código desde el Sprint 5

### HU18 + HU19 – Apariencia (`Servicios/ServicioApariencia.cs`, `Servicios/Paleta.cs`)
- `ServicioApariencia` guarda el tema (`Tema.Sistema / Claro / Oscuro`) y la paleta para daltonismo en `Preferences`, y dispara `EventoApariencia.APARIENCIA_CAMBIADA` con una **tercera instancia del despachador genérico**: `Despachador<EventoApariencia, Apariencia>`.
- Los colores de estado son recursos con nombre: `ColorExito`, `ColorExitoFondo`, `ColorPeligro`, `ColorPeligroBorde`, `ColorAviso`. **En XAML usar siempre `{DynamicResource ColorExito}`** (no colores fijos como `#2E7D32`), así cambian solos con la paleta. En código: `label.SetDynamicResource(Label.TextColorProperty, "ColorAviso")`.
- Estilos reutilizables en `Styles.xaml`: `TarjetaViaje`, `TextoSecundario`, `Etiqueta`, `BotonPrincipal`, `BotonSecundario`, `BotonPeligro`.

### HU25 – Chat grupal (`Servicios/ServicioChat.cs`, `ChatPage`)
- `/chats/{viajeId}` en Firebase (solo los participantes leen y escriben). `ServicioChat.AvisarAlGrupoAsync(viaje, "se sumó al viaje")` manda un mensaje automático.
- Con el chat cerrado, un mensaje nuevo dispara `ViajeEvento.MENSAJE_RECIBIDO`.

### Mapas y ruta (`Servicios/Geocodificador.cs`, `ServicioEncuentros.cs`, `ServicioMapas.cs`)
- `Geocodificador.BuscarLugarAsync(texto, cerca, zonaRespaldo)` devuelve un `LugarEncontrado` (punto, localidad, provincia y nombre). Primero busca en **Nominatim** y, si no lo encuentra, en **Photon**, que tolera errores de tipeo. Entiende siglas ("facu ubp" → "Universidad Blas Pascal").
- Los lugares encontrados se guardan en el dispositivo (`lugares.json`): la segunda vez "Ver ruta" sale al instante.
- Cada punto de encuentro guarda su **zona** ("Villa Allende, Córdoba") para que Google Maps lo encuentre. Ya no se le agrega el origen del viaje.
- En Publicar, al salir de Origen, Destino o "Desde dónde salís" se muestra debajo si el lugar aparece en el mapa (no frena la publicación).

### Reglas de Firebase actuales
Nodos: `viajes`, `pines`, `encuentros` (con `zona` opcional), `chats`. El archivo de `main` es el que está publicado.

---

## 4. Historias de Francisco (Compañero 1)

### HU26 (US E4-26) – Centro de notificaciones
**Como** usuario, **quiero** ver los avisos de mis viajes en cualquier pantalla y poder revisarlos después, **para** no perderme novedades aunque no esté en "Viajes disponibles".

Criterios de aceptación:
- Los avisos que hoy salen en "Viajes disponibles" (alguien se anotó o se bajó, cancelación, reemplazo, costo, inicio, no-show, mensaje nuevo) aparecen como un **cartel arriba en cualquier pantalla** y se van solos a los pocos segundos.
- En todas las pantallas principales hay una **campanita** con la cantidad de avisos sin leer.
- Al tocarla se abre el **centro de notificaciones**: lista con la hora, los no leídos resaltados, "Marcar todo como leído". Tocar un aviso lleva a ese viaje.
- Los avisos se guardan en el dispositivo (siguen ahí al volver a abrir la app); se guardan los últimos 50.

Diseño:
- `Servicios/ServicioNotificaciones.cs`: se registra en el despachador de viajes, convierte cada evento con `ConversorMensajes` y guarda un `record Notificacion(Id, ViajeId, Texto, Hora, Leida)`. Dispara su propio evento `EventoNotificacion.NOTIFICACION_NUEVA` (otra instancia de `Despachador<,>`) al que se suscriben el cartel y la campanita.
- El cartel es un control reutilizable en `Controles/` que se agrega a cada pantalla.
- La oferta de reemplazo ("¿Querés anotarte?") se sigue mostrando como cartel con botones, en la pantalla que esté abierta.

### HU29 (US E11-29) – Rediseño de las tarjetas de viaje
**Como** usuario, **quiero** que las tarjetas de los viajes se lean de un vistazo, **para** encontrar rápido lo que me sirve.

Criterios de aceptación:
- Datos cortos como **chips**: hora, asientos libres, costo.
- **Avatares con iniciales** superpuestos para el conductor y los pasajeros (colores por persona, siempre con su nombre accesible).
- Arriba de "Viajes disponibles", una tarjeta **"Tu próximo viaje"** con hora, ruta y botones de Ruta y Chat.
- Respeta tema claro/oscuro y la paleta para daltonismo (solo `DynamicResource` y `AppThemeBinding`).

### HU30 (US E1-30) – Viajes que coinciden con mi horario y mi zona
**Como** pasajero, **quiero** ver primero los viajes que salen cerca de mi horario habitual y de mi zona, **para** no tener que revisar toda la lista.

Criterios de aceptación:
- La app aprende mi horario habitual de los viajes que hice (o lo cargo en Mi perfil).
- Los viajes que salen ±1 hora de ese horario y desde mi zona muestran la etiqueta **"Coincide con tu horario"** y aparecen primero.
- Usa el método genérico `Busqueda.MasCercano<T, TDistancia>` (el mismo del reemplazo).

---

## 5. Historias de Octavio (Compañero 2)

### HU28 (US E1-28) – Rutina fija
**Como** conductor, **quiero** publicar de una vez un viaje que hago siempre (por ejemplo, lunes y miércoles a las 8), **para** no tener que cargarlo cada semana.

Criterios de aceptación:
- En Publicar hay una opción **"Repetir"**: se eligen los días de la semana (L a S) y una fecha "hasta" (como máximo 16 semanas, un cuatrimestre).
- Se publica un viaje por cada fecha, con los mismos datos y un **`SerieId`** en común (campo nuevo en `ViajeDatos` y en las reglas).
- Las fechas que chocan con otro viaje mío (`HorarioOcupadoException`) o que ya pasaron se saltean, y al final se muestra un resumen: "Se publicaron 14 viajes. Se salteó el 03/11 porque ya tenés otro viaje a esa hora".
- En Mis viajes, al cancelar un viaje de una serie se pregunta **"Solo este" / "Todos los que quedan"**.
- Validaciones: ningún día elegido o fecha "hasta" anterior a la de inicio → `ArgumentException`; más de 16 semanas → `ArgumentOutOfRangeException`.
- Idea para la materia: un método genérico que arme las fechas (por ejemplo `Repeticiones.Fechas(desde, hasta, dias)`), con pruebas.

---

### HU31 (US E6-31) – Calificaciones
**Como** usuario, **quiero** calificar a las personas con las que viajé, **para** que la reputación de cada uno se construya viaje a viaje y saber con quién me anoto.

Criterios de aceptación:
- Cuando un viaje está **Finalizado** (HU15), en Mis viajes → "Ver viajes anteriores" aparece **"Calificar"** para el conductor y los pasajeros que viajaron (los que estuvieron a bordo).
- Se elige de 1 a 5 estrellas para cada uno de los otros participantes (comentario opcional, hasta 200 caracteres). Se califica **una sola vez por viaje** a cada persona; intentar de nuevo → excepción propia (por ejemplo `YaCalificasteException`).
- El perfil y las tarjetas de viaje muestran el **promedio y la cantidad** ("★ 4,6 (12)") del conductor. Sin calificaciones: "Nuevo".
- Recibir una calificación dispara un evento (por ejemplo `CALIFICACION_RECIBIDA`) con su mensaje en `ConversorMensajes` ("Octavio te calificó con 5 estrellas").
- Reglas de Firebase (nodo `/calificaciones/{calificadoUid}/{viajeId}_{autorUid}`): solo escribe quien participó del viaje finalizado, no se puede calificar a sí mismo, `estrellas` entre 1 y 5, no se puede modificar después. Partir del `database.rules.json` de `main` y pasárselo a Francisco para publicar.
- Validaciones: estrellas fuera de 1–5 → `ArgumentOutOfRangeException`; viaje no finalizado o no viajó → `ViajeException` correspondiente.
- Idea para la materia: el promedio con un método genérico (por ejemplo `Estadisticas.Promedio<T>(lista, selector)`).

## 6. Backlog futuro

- **HU9** – Ver cuántas veces viajé antes con otra persona.
- **HU20** – Ingreso con Google.
- **HU27** (US E6-27) – Registro solo con mail de la facultad. Se deja para más adelante: limita las pruebas, la demo y a usuarios de otras facultades. Si se retoma, que sea con un interruptor para apagarlo.

---

## 7. Definición de "Hecho"

- [ ] Compila sin errores y anda en Windows (y en Android si se puede).
- [ ] Se probó con dos usuarios.
- [ ] Los errores se manejan con las excepciones del proyecto y quedan en el registro.
- [ ] Los colores usan `DynamicResource` / `AppThemeBinding` (se ve bien en claro, oscuro y daltonismo).
- [ ] Los eventos nuevos tienen su mensaje en `ConversorMensajes`.
- [ ] Pull request mergeado en `main` y tarjeta de Planner en "Hecho".

---

## 8. Para empezar con Claude

> Estoy trabajando en el proyecto Pool Compartido (.NET MAUI + Firebase). Leé `docs/Sprint4.md` (convenciones), `docs/Sprint5.md` (estructura y qué se hizo) y `docs/Sprint6.md` (qué cambió y las historias de este sprint). Voy a hacer la **HU28** en la rama `hu-28-octa`. Antes de escribir código, revisá los archivos que vas a tocar y contame qué pensás cambiar. Respetá las convenciones de excepciones, eventos y estilo, usá `DynamicResource` para los colores y no toques `ClavesFirebase.cs`. Si hay que cambiar las reglas de Firebase, partí del `database.rules.json` de `main`.
