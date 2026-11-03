# Sprint 6 – Pool Compartido

Guía del sprint para el equipo: **Compañero 1 = Francisco (Díaz)** y **Compañero 2 = Octavio (Brachetti)**. También está pensada para pasársela a Claude como contexto. Las convenciones generales (excepciones, estilo, ramas) siguen siendo las de `docs/Sprint4.md`, y la estructura del código está en `docs/Sprint5.md`. Este documento agrega lo que cambió desde entonces y las historias nuevas.

---

## 1. Objetivo del sprint

Pulir la experiencia: que ningún aviso se pierda, que la app se vea moderna, que sirva para el día a día de un estudiante (siempre los mismos días y horarios) y que se pueda confiar en con quién viajás. Varias ideas salen de mirar una app parecida (PUL, pul.com.ar).

> **Sprint cerrado: todas las historias están hechas y mergeadas en `main`.**

| HU | Qué es | Quién | Estado |
|---|---|---|---|
| HU26 | Centro de notificaciones (avisos en todas las pantallas + campanita) | Compañero 1 (Francisco) | HECHA |
| HU29 | Rediseño de las tarjetas de viaje (avatares, chips, "Tu próximo viaje") | Compañero 1 (Francisco) | HECHA |
| HU30 | Mis horarios: viajes que coinciden con mi rutina | Compañero 1 (Francisco) | HECHA |
| HU32 | Teléfono de contacto: llamar o WhatsApp desde la app (agregada durante el sprint) | Compañero 1 (Francisco) | HECHA |
| HU28 | Rutina fija: publicar un viaje que se repite toda la semana o el cuatrimestre | Compañero 2 (Octavio) | HECHA |
| HU31 | Calificaciones después del viaje | Compañero 2 (Octavio) | HECHA |

El backlog quedó solo con las historias que hicimos. HU9, HU20, HU27 y la vieja HU33 se sacaron y sus números quedan libres. La HU33 ahora es "Pulido de la interfaz y marca Coopul" (Sprint 7, ver sección 6).

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
- Los colores de estado son recursos con nombre: `ColorExito`, `ColorExitoFondo`, `ColorPeligro`, `ColorPeligroBorde`, `ColorAviso`. **En XAML usar siempre `{DynamicResource ColorExito}`** (no colores fijos como `#2E7D32`), así cambian solos con la paleta. Para el resto de los colores ver la sección 6 (Sprint 7). En código: `label.SetDynamicResource(Label.TextColorProperty, "ColorAviso")`.
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
Nodos (al cierre del sprint): `viajes` (con `serieId`), `pines`, `encuentros` (con `zona` opcional), `chats`, `horarios` (HU30), `telefonos` y `contactos` (HU32) y `calificaciones` (HU31). El archivo de `main` es el que está publicado.

---

## 4. Historias de Francisco (Compañero 1)

### HU26 (US E4-26) – Centro de notificaciones · HECHA
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

### HU29 (US E11-29) – Rediseño de las tarjetas de viaje · HECHA
**Como** usuario, **quiero** que las tarjetas de los viajes se lean de un vistazo, **para** encontrar rápido lo que me sirve.

Criterios de aceptación:
- Datos cortos como **chips**: hora, asientos libres, costo.
- **Avatares con iniciales** superpuestos para el conductor y los pasajeros (colores por persona, siempre con su nombre accesible).
- Arriba de "Viajes disponibles", una tarjeta **"Tu próximo viaje"** con hora, ruta y botones de Ruta y Chat.
- Respeta tema claro/oscuro y la paleta para daltonismo (solo `DynamicResource` y `AppThemeBinding`).

### HU30 (US E1-30) – Mis horarios · HECHA
**Como** pasajero, **quiero** cargar los días y horarios en que viajo siempre, **para** ver primero los viajes que me sirven y enterarme cuando publican uno.

Cómo quedó:
- **Mi perfil → Mis horarios** (`HorarioPage`): días de la semana, hora, desde (opcional) y hacia. Hasta 10 horarios, guardados en Firebase en `/horarios/{uid}` (solo los lee y escribe el dueño).
- Un viaje **coincide** si sale ese día, con ±1 hora de diferencia y va al mismo lugar (compara palabras sin tildes y entiende siglas: "UBP" = "Universidad Blas Pascal"). Esos viajes aparecen primero con **"⭐ Coincide con tu horario del lunes 07:30"**.
- `ServicioHorarios` usa el método genérico `Busqueda.MasCercano<T, TDistancia>` (el mismo del reemplazo, HU6) para elegir el horario que mejor coincide.
- Evento nuevo `VIAJE_COINCIDENTE`, **encadenado** a `VIAJE_PUBLICADO` (HU11): el centro de notificaciones avisa "⭐ Nuevo viaje para tu horario del…".
- Excepciones: sin días o sin destino → `ArgumentException`; más de 10 horarios → `ArgumentOutOfRangeException`.

## 4b. Agregados durante el sprint (Francisco)

### HU32 (US E4-32) – Teléfono de contacto · HECHA
- Mi perfil → **Teléfono (opcional)**: se normaliza solo al formato internacional ("351 123 4567" → "+5493511234567"); un texto inválido → `FormatException`.
- Privacidad: se guarda en `/telefonos/{uid}` (solo el dueño) y se copia a `/contactos/{viajeId}/{uid}` solo en los viajes donde participa; las reglas solo dejan leerlo a los de ese viaje. Se borra al salirse.
- Mis viajes → **Contactar**: 📞 Llamar o 💬 WhatsApp (abre el chat con un saludo) a cada persona del viaje que cargó su número.

### Mejoras de las tarjetas (ideas de Carpoolear)
- Recorrido en vertical (origen arriba, destino abajo, con puntos y línea).
- Lugares libres con color según cuántos quedan (verde / naranja / gris "Completo"), siempre con texto.
- Filtros **Desde ⇄ Hacia** (el botón invierte, para buscar la vuelta), **Día** y franja; sin distinguir tildes ni mayúsculas.
- "Tu próximo viaje" se va con el scroll en el celular (va como encabezado de la lista).

### Búsqueda de lugares más confiable (`Geocodificador`)
- Si Nominatim no encuentra un lugar, se prueba con **Photon** (tolera errores de tipeo).
- Prioriza resultados en **Córdoba** (`Configuracion.ZonaPorDefecto`) y respeta la provincia escrita ("utn cba"), sin confundir calles como "Av. Córdoba".
- Los lugares encontrados se guardan en el dispositivo (`lugares_v2.json`): la ruta sale al instante la segunda vez.
- En Publicar, debajo de cada campo se ve si el lugar aparece en el mapa (no frena la publicación).

### Arreglos
- El conductor puede **finalizar el viaje** desde la hora de salida aunque no haya tocado "Iniciar viaje".
- "Usar mi ubicación" ya no se queda en "Buscando..." en Windows (tope de tiempo propio).
- El punto de encuentro compartido con GPS dice "Ubicación compartida" (antes decía "Mi ubicación" y confundía al conductor).
- El cartel de notificaciones ya no se corta en Windows.

---

## 5. Historias de Octavio (Compañero 2)

### HU28 (US E1-28) – Rutina fija · HECHA
**Como** conductor, **quiero** publicar de una vez un viaje que hago siempre (por ejemplo, lunes y miércoles a las 8), **para** no tener que cargarlo cada semana.

Criterios de aceptación:
- En Publicar hay una opción **"Repetir"**: se eligen los días de la semana (L a S) y una fecha "hasta" (como máximo 16 semanas, un cuatrimestre).
- Se publica un viaje por cada fecha, con los mismos datos y un **`SerieId`** en común (campo nuevo en `ViajeDatos` y en las reglas).
- Las fechas que chocan con otro viaje mío (`HorarioOcupadoException`) o que ya pasaron se saltean, y al final se muestra un resumen: "Se publicaron 14 viajes. Se salteó el 03/11 porque ya tenés otro viaje a esa hora".
- En Mis viajes, al cancelar un viaje de una serie se pregunta **"Solo este" / "Todos los que quedan"**.
- Validaciones: ningún día elegido o fecha "hasta" anterior a la de inicio → `ArgumentException`; más de 16 semanas → `ArgumentOutOfRangeException`.
- Idea para la materia: un método genérico que arme las fechas (por ejemplo `Repeticiones.Fechas(desde, hasta, dias)`), con pruebas.

---

### HU31 (US E6-31) – Calificaciones · HECHA
**Como** usuario, **quiero** calificar a las personas con las que viajé, **para** que la reputación de cada uno se construya viaje a viaje y saber con quién me anoto.

Criterios de aceptación:
- Cuando un viaje está **Finalizado** (HU15), en Mis viajes → "Ver viajes anteriores" aparece **"Calificar"** para el conductor y los pasajeros que viajaron (los que estuvieron a bordo).
- Se elige de 1 a 5 estrellas para cada uno de los otros participantes (comentario opcional, hasta 200 caracteres). Se califica **una sola vez por viaje** a cada persona; intentar de nuevo → excepción propia (por ejemplo `YaCalificasteException`).
- El perfil y las tarjetas de viaje muestran el **promedio y la cantidad** ("★ 4,6 (12)") del conductor. Sin calificaciones: "Nuevo".
- Recibir una calificación dispara un evento (por ejemplo `CALIFICACION_RECIBIDA`) con su mensaje en `ConversorMensajes` ("Octavio te calificó con 5 estrellas").
- Reglas de Firebase (nodo `/calificaciones/{calificadoUid}/{viajeId}_{autorUid}`): solo escribe quien participó del viaje finalizado, no se puede calificar a sí mismo, `estrellas` entre 1 y 5, no se puede modificar después. Partir del `database.rules.json` de `main` y pasárselo a Francisco para publicar.
- Validaciones: estrellas fuera de 1–5 → `ArgumentOutOfRangeException`; viaje no finalizado o no viajó → `ViajeException` correspondiente.
- Idea para la materia: el promedio con un método genérico (por ejemplo `Estadisticas.Promedio<T>(lista, selector)`).

## 6. Sprint 7: pulido de la interfaz y marca Coopul (Francisco)

- **HU33** (US E11-33) – Pulido de la interfaz y marca Coopul. **HECHA.**
- La app pasó a llamarse **Coopul**: logo en el login y en la barra de arriba, ícono, pantalla de carga y paleta azul (`#0044EB`).
- Modo claro y oscuro revisados (contraste WCAG AA) y cambio de tema y de paleta para daltonismo en vivo, sin reiniciar.
- Ajustes de Android: cajas de los campos, barra de estado y de navegación, botones del calendario y estado vacío de las listas.
- Mejoras de uso: filtros en una fila en pantallas anchas, 📍 para la ubicación en "Desde", flecha para volver atrás en Windows y sin pestañas en las pantallas internas.

### Colores (reemplaza lo de la sección 3)

- Los colores del tema están en `Colors.xaml` en pares `...Claro` / `...Oscuro`. En XAML van con `AppThemeBinding` (por ejemplo `{AppThemeBinding Light={StaticResource ColorTextoClaro}, Dark={StaticResource ColorTextoOscuro}}`). En código se usa `ColoresDelTema.Aplicar(...)`.
- Solo los colores de estado (`ColorExito`, `ColorExitoFondo`, `ColorPeligro`, `ColorPeligroBorde`, `ColorAviso`, `ColorAvisoFondo`, `ColorSobrePeligro`) van con `DynamicResource`, porque también cambian con la paleta para daltonismo.
- `RefrescoDeTema` vuelve a aplicar los estilos de las pantallas abiertas cuando cambia el tema (MAUI no lo hace bien solo).

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
