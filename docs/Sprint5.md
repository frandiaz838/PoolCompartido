# Sprint 5 – Pool Compartido

Guía del sprint para el equipo: **Compañero 1 = Francisco (Díaz)** y **Compañero 2 = Octavio (Brachetti)**. También está pensada para pasársela a Claude como contexto. Las convenciones generales (excepciones, estilo, ramas) siguen siendo las de `docs/Sprint4.md`; este documento agrega lo que cambió en el código y las historias nuevas.

---

## 1. Objetivo del sprint

Terminar las funciones que hacen a la app usable de verdad: dónde te pasan a buscar, usar la ubicación, repartir el costo, el ciclo de vida real del viaje (check-in y no-show) y la apariencia (modo oscuro y daltónico).

| HU | Qué es | Quién | Orden |
|---|---|---|---|
| HU24 | Punto de encuentro privado | Compañero 1 (Francisco) | 1 |
| HU23 | Usar mi ubicación (depende de HU24) | Compañero 1 (Francisco) | 2 |
| HU18 | Tema claro / oscuro / del sistema | Compañero 1 (Francisco) | 3 |
| HU19 | Paleta para daltonismo | Compañero 1 (Francisco) | 4 |
| HU14 | Reparto del costo entre los pasajeros | Compañero 2 (Octavio) | 1 |
| HU15 | Check-in y ciclo de vida del viaje | Compañero 2 (Octavio) | 2 |
| HU16 | Detección de no-show (depende de HU14 y HU15) | Compañero 2 (Octavio) | 3 |
| HU25 | Chat grupal del viaje (agregada durante el sprint) | Compañero 1 (Francisco) | 5 |

Backlog futuro, si sobra tiempo: HU9 (confianza entre usuarios) y HU20 (ingreso con Google).

---

## 2. Cómo trabajamos

Igual que en el Sprint 4, simplificado:

1. **Una rama por HU**, sin espacios: `hu-24-fran`, `hu-14-octa`, etc. Se crea desde `main` después de hacer Pull.
2. **Commit + push** seguido. Al terminar: pull request en GitHub y merge.
3. **Después de cada merge**, los dos vuelven a `main` y hacen Pull. Si tenés una rama abierta, combiná `main` en tu rama (Git → Administrar ramas → clic derecho en `main` → Combinar en la rama actual).
4. **Probar con dos usuarios** (una PC cada uno) antes del merge.
5. **`Servicios/ClavesFirebase.cs` no está en GitHub.** No borrarlo ni subirlo.

### Archivos que tocamos los dos (avisar por WhatsApp antes)

| Archivo | Francisco | Octavio |
|---|---|---|
| `Servicios/DatosFirebase.cs` (`ViajeDatos` y `ConversorViajes`) | `PuntoPartida` (HU24) | `CostoTotal` (HU14), estado del viaje y `ABordo` (HU15) |
| `Modelos/Viaje.cs` | `PuntoPartida` | costo, estado real, a bordo |
| `PublicarViajePage.xaml(.cs)` | campo "Punto de partida" (HU24) | campo "Costo total" (HU14) |
| `MisViajesPage.xaml(.cs)` | puntos de encuentro de los pasajeros (HU24) | botones Iniciar / Finalizar / PIN (HU15) |
| `Servicios/ConversorMensajes.cs` | — | mensajes de los eventos nuevos |
| `database.rules.json` + consola de Firebase | nodo `/encuentros` (HU24) | campos nuevos en `/viajes` (HU14, HU15) |

Son líneas distintas, así que Git las combina solo, pero **si los dos agregan propiedades en el mismo lugar del mismo archivo, va a haber conflicto**. Para evitarlo: agreguen sus propiedades al **final** del bloque correspondiente y mergeen seguido, no todo al final del sprint.

**Reglas de Firebase:** las cambia uno a la vez. Se edita `database.rules.json`, se revisa, y **Francisco** (dueño del proyecto en Firebase) las pega en la consola → Realtime Database → Reglas → Publicar.

---

## 3. Qué cambió en el código desde el Sprint 4

Leer `docs/Sprint4.md` para la estructura general. Lo nuevo:

### Estado del viaje según la hora (`Modelos/Viaje.cs`)
- `enum EstadoViaje { Abierto, PorSalir, EnCurso, Cancelado, Finalizado }`.
- `Viaje.Salida` = fecha + hora de salida. `Viaje.Estado` se calcula con la hora actual (`EstadoEn(DateTime ahora)` para poder probarlo).
- **Abierto** hasta 5 minutos antes (`Viaje.CierreAnotaciones`). **PorSalir** en los últimos 5 minutos: no se anota nadie nuevo, pero todavía se puede salir o cancelar. **EnCurso** desde la salida y durante 2 horas (`Viaje.DuracionEnCurso`), y después **Finalizado**, que se oculta de la app.
- Propiedades para bindings: `AceptaPasajeros`, `SePuedeModificar`, `Terminado`, `EstadoTexto`, `MostrarEstado`, `PuedeAnotarse`, `PuedeCancelar`, `PuedeSalirse`.
- El timer de `App` llama a `ServicioViajes.ActualizarEstados()` cada 5 segundos; si algún viaje cambió de estado, dispara el evento .NET **`EstadosCambiados`** y las pantallas se redibujan.

### Reglas de negocio nuevas (`ServicioViajes`)
- Anotarse a menos de 5 minutos de la salida: `AnotacionesCerradasException`.
- Salirse o cancelar un viaje que ya salió: `ViajeVencidoException` ("Este viaje ya salió").
- **Nadie puede estar en dos viajes que salen a menos de una hora** (`ServicioViajes.SeparacionMinima`), ni como conductor ni como pasajero: `HorarioOcupadoException` (tiene la propiedad `Otro` con el viaje que choca). Lo controla `ViajeQueSeSuperpone(salida, exceptoId)`. Se aplica al publicar y al anotarse.
- Publicar un viaje que sale en menos de 5 minutos: `ArgumentOutOfRangeException`.

### HU7 – Conversión de mensajes (`Servicios/ConversorMensajes.cs`)
- `ConversorMensajes.Convertir(evento, viaje)` devuelve el texto según el rol (`RolEnViaje { Ninguno, Conductor, Pasajero }`) o `null` si no corresponde avisar. **Todo evento nuevo tiene que agregar acá su mensaje.**
- `MainPage` registra una sola callback, `AlOcurrirEvento`, para todos los eventos de viajes.

### HU6 + HU11 – Reemplazo y eventos encadenados
- `ServicioViajes` se suscribe a su propia tabla de eventos: al recibir `VIAJE_CANCELADO`, si el usuario era pasajero, busca un reemplazo y dispara `REEMPLAZO_ENCONTRADO` o `SIN_REEMPLAZO`. El despachador lo encola y lo procesa después: eso es el encadenamiento.
- **Ojo:** `Reiniciar()` crea un despachador nuevo, así que las suscripciones del propio servicio se vuelven a registrar ahí (`RegistrarBusquedaDeReemplazo()`). Si agregan otra callback del servicio (por ejemplo, `COSTO_RECALCULADO`), regístrenla en ese mismo lugar.
- Búsqueda genérica: `Busqueda.MasCercano<T, TDistancia>(candidatos, sirve, distancia)` en `Servicios/BuscadorDeReemplazo.cs`. Se puede reutilizar.

### HU4 y HU21 (Octavio)
- `SalirseAsync` (ETag, dispara `ASIENTO_LIBERADO`) y `DejarMisViajesAsync` (al borrar la cuenta deja solo los viajes que todavía no salieron).

### Reglas actuales de Firebase
`/viajes/{id}`: lectura para usuarios logueados. Escritura en cuatro casos: crear (conductor), el conductor modifica su viaje, un pasajero se agrega (asientos − 1) y un pasajero se quita (asientos + 1). En los dos últimos, los datos del viaje no pueden cambiar. **Cada campo nuevo que un pasajero no deba poder tocar hay que agregarlo a esas dos condiciones** (por ejemplo, `costoTotal`).

---

## 4. Historias de Francisco (Compañero 1)

### HU24 (US E2-24) – Punto de encuentro privado
**Como** pasajero, **quiero** indicar dónde me pasan a buscar, **para** que el conductor sepa a dónde ir sin que mi dirección quede a la vista de todos.

Criterios de aceptación:
- Al anotarse, el pasajero puede cargar su punto de encuentro como texto ("Esquina de Av. Colón y General Paz"). También se puede cargar o cambiar después, desde Mis viajes.
- Al publicar, el conductor puede indicar un **punto de partida público** opcional (un lugar público, no su casa). Se ve en la lista.
- El punto de cada pasajero se guarda **aparte del viaje**, en `/encuentros/{viajeId}/{pasajeroUid}`, con una regla que permite leerlo **solo al conductor de ese viaje y al pasajero que lo cargó**.
- En Mis viajes, el conductor ve la lista de pasajeros con su punto y un botón para abrirlo en Google Maps (reusar `ServicioMapas`).
- Al salirse de un viaje (HU4) se borra su punto de encuentro.

Pistas técnicas:
- Regla sugerida:
  ```json
  "encuentros": {
    "$viajeId": {
      "$uid": {
        ".read": "auth != null && (auth.uid === $uid || root.child('viajes/' + $viajeId + '/conductorUid').val() === auth.uid)",
        ".write": "auth != null && auth.uid === $uid"
      }
    }
  }
  ```
- Métodos nuevos en `RepositorioFirebase` (GET/PUT/DELETE a `/encuentros/...json?auth=`), con el mismo manejo de errores que los otros.
- Para pedir el texto: `DisplayPromptAsync`. Texto vacío o de más de 100 caracteres: `ArgumentException`.

Prioridad: Should · Estimación: S

### HU23 (US E1-23) – Usar mi ubicación
**Como** usuario, **quiero** usar mi ubicación actual al publicar o al anotarme, **para** no tener que escribir de dónde salgo.

Criterios de aceptación:
- Botón **"Usar mi ubicación"** al lado del origen (al publicar) y del punto de encuentro (al anotarse).
- En la parte pública se muestra **solo el barrio o la localidad** ("Villa Allende"), nunca la dirección exacta. La posición exacta se guarda solo como punto de encuentro privado (HU24).
- Se manejan los errores reales: permiso denegado (`PermissionException`), GPS apagado (`FeatureNotEnabledException`), dispositivo sin GPS (`FeatureNotSupportedException`) y sin internet para buscar el barrio (`SinConexionException`).

Pistas técnicas:
- `Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)))`.
- Barrio con OpenStreetMap, gratis y sin clave: `https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat={lat}&lon={lon}&zoom=14`. **Es obligatorio mandar un `User-Agent`** (por ejemplo `PoolCompartido/1.0`). Del JSON usar `address.suburb`, `address.city_district`, `address.town` o `address.city`, en ese orden.
- Permisos: Android → `ACCESS_COARSE_LOCATION` y `ACCESS_FINE_LOCATION` en `Platforms/Android/AndroidManifest.xml`. Windows → capacidad `location` en `Platforms/Windows/Package.appxmanifest`.
- Poner todo en una clase `Servicios/ServicioUbicacion.cs`, no en la pantalla.

Prioridad: Should · Estimación: M · Depende de: HU24

### HU18 (US E11-18) – Tema claro, oscuro o del sistema
**Como** usuario, **quiero** elegir entre tema claro, oscuro o el del sistema, **para** usar la app cómodamente según la luz o mi preferencia.

Criterios de aceptación:
- En Mi perfil, sección **"Apariencia"**: Claro / Oscuro / Según el sistema.
- Se aplica al instante y se recuerda al volver a abrir la app.

Pistas técnicas:
- `Application.Current.UserAppTheme = AppTheme.Light / Dark / Unspecified`.
- Guardar con `Preferences.Default.Set("tema", ...)` y aplicarlo en el constructor de `App`.
- Los colores ya usan `AppThemeBinding` en casi todos lados; revisar que no quede ningún color fijo que se vea mal en el tema claro.

Prioridad: Could · Estimación: S

### HU19 (US E11-19) – Paleta para daltonismo
**Como** usuario con daltonismo, **quiero** activar una paleta de colores adaptada, **para** poder distinguir bien los estados de los viajes.

Criterios de aceptación:
- Un interruptor en "Apariencia" activa una paleta que no combina rojo con verde.
- Ningún estado se indica solo con color: siempre hay texto ("En curso", "Cancelado") o ícono.
- El cambio dispara `APARIENCIA_CAMBIADA` y las pantallas se redibujan. Se recuerda al volver a abrir la app.

Pistas técnicas:
- Hoy los colores de estado están fijos en el XAML: rojo `#E5484D` (cancelado), verde `#2E9E5B` / `#4CAF50` (en curso, anotado) y naranja `#D9822B` (por salir). Pasarlos a **recursos con nombre** (`ColorPeligro`, `ColorExito`, `ColorAviso`) en `App.xaml` y usarlos con `{DynamicResource ...}`. Al activar la paleta se cambian los valores de esos recursos.
- Paleta sugerida (Okabe-Ito, pensada para daltonismo): azul `#0072B2`, naranja `#E69F00`, bermellón `#D55E00`.
- Para el evento: un enum `EventoApariencia { APARIENCIA_CAMBIADA }` y una **tercera instancia del despachador genérico**, `Despachador<EventoApariencia, bool>`. Es un buen ejemplo de reutilizar el genérico para la defensa.
- **Octavio:** si agregás colores nuevos en la HU15, avisá. Cuando esté la HU19, usá esos recursos en lugar de colores fijos.

Prioridad: Could · Estimación: M

---

## 5. Historias de Octavio (Compañero 2)

### HU14 (US E8-14) – Reparto del costo del viaje
**Como** conductor, **quiero** fijar el costo total del viaje (nafta + peajes), **para** que se divida automáticamente entre los que viajan.

**Decisión tomada:** el costo se divide **solo entre los pasajeros**. El conductor no paga su parte.

Criterios de aceptación:
- Al publicar, el conductor carga el **costo total** (opcional; vacío = viaje sin costo).
- En las listas se ve el costo por pasajero ("$2.000 por pasajero"). Si todavía no hay pasajeros, se muestra el total y cuánto pagaría quien se anote ("Si te anotás: $6.000").
- Cada `ASIENTO_RESERVADO` o `ASIENTO_LIBERADO` en un viaje con costo dispara **`COSTO_RECALCULADO`** (encadenado, igual que el reemplazo), que avisa el nuevo importe. Conductor: "Ahora cada pasajero paga $2.000". Pasajero: "Ahora pagás $2.000 (son 3 pasajeros)".
- Errores: texto no numérico → `FormatException`; costo negativo o absurdo (por ejemplo, más de $1.000.000) → `ArgumentOutOfRangeException`.

Pistas técnicas:
- Campo `costoTotal` (número) en `/viajes/{id}`: propiedad en `ViajeDatos`, `ConversorViajes` y `Viaje`.
- La suscripción del servicio a `ASIENTO_RESERVADO` y `ASIENTO_LIBERADO` va junto a `RegistrarBusquedaDeReemplazo()`, porque se tiene que volver a registrar en `Reiniciar()`.
- Mensajes en `ConversorMensajes`, y el evento nuevo en `Eventos/ViajeEvento.cs`.
- Reglas: agregar `newData.child('costoTotal').val() === data.child('costoTotal').val()` a las condiciones de anotarse y salirse, para que un pasajero no pueda cambiar el costo.

Prioridad: Should · Estimación: S

### HU15 (US E9-15) – Check-in y ciclo de vida del viaje
**Como** conductor, **quiero** marcar el inicio y el fin del viaje y confirmar quién subió, **para** que el viaje tenga estados reales y quede registrado.

Criterios de aceptación:
- En Mis viajes, el conductor tiene **"Iniciar viaje"** (desde 15 minutos antes de la salida) y después **"Finalizar viaje"**. Se disparan `VIAJE_INICIADO` y `VIAJE_FINALIZADO`.
- Cada pasajero ve en Mis viajes un **PIN de 4 dígitos** para ese viaje. Al subir, se lo dice al conductor, que lo ingresa: se dispara `PASAJERO_A_BORDO`. PIN incorrecto → `PinInvalidoException`.
- Con el viaje finalizado, pasa al historial.

Pistas técnicas:
- **Integrar con el estado por hora que ya existe**, no reemplazarlo. Si el conductor inició, el viaje está en curso aunque no haya llegado la hora. Si finalizó, está finalizado. Si nunca toca los botones, sigue funcionando como ahora, por la hora. La lógica va en `Viaje.EstadoEn(...)`.
- Campos sugeridos en `/viajes/{id}`: `iniciado` (bool), `finalizado` (bool), `aBordo { uid: true }`. Solo los escribe el conductor; la regla del conductor ya lo permite.
- **El PIN no puede quedar a la vista del conductor**, si no el check-in no prueba nada. Opción simple:
  - Al anotarse, la app del pasajero genera un PIN al azar y lo guarda en un lugar que **solo él puede leer**: `/pines/{uid}/{viajeId}`, con regla de lectura y escritura `auth.uid === $uid`.
  - En el viaje se guarda solo el **hash** del PIN (`pinHash/{uid}`, SHA-256 con `System.Security.Cryptography`), en el mismo guardado que lo anota.
  - El conductor escribe el PIN que le dicen y la app compara los hashes.

  Con 4 dígitos no es seguridad de banco, pero para el alcance del proyecto alcanza.
- Hoy los viajes finalizados se ocultan de Mis viajes. Para el "historial" alcanza con un interruptor "Ver viajes anteriores" en Mis viajes que muestre también los finalizados.

Prioridad: Should · Estimación: L

### HU16 (US E9-16) – Detección de no-show
**Como** conductor, **quiero** que si un pasajero no sube en 10 minutos se libere su asiento, **para** no esperar de más.

Criterios de aceptación:
- Después de `VIAJE_INICIADO`, si un pasajero no hizo check-in en el tiempo de espera, se dispara `NO_SHOW_DETECTADO`, se lo saca del viaje y se libera su asiento.
- Eso dispara `ASIENTO_LIBERADO` y, con la HU14, `COSTO_RECALCULADO`. Es la **cadena de eventos más larga del proyecto**: mostrarla en la defensa.
- El tiempo de espera es configurable (10 minutos normalmente, 1 minuto para la demo).

Pistas técnicas:
- Usar un `IDispatcherTimer` o revisar en `ActualizarEstados()`, que ya corre cada 5 segundos.
- Lo ejecuta **solo la app del conductor**, que es quien puede modificar el viaje.
- Mensaje para el pasajero que quedó afuera: "No llegaste a subir al viaje de Juan y se liberó tu lugar".

Prioridad: Could · Estimación: M · Depende de: HU14 y HU15

---

## 6. Agregados durante el sprint

### Ruta con paradas y ruta óptima (mejora de la HU24) · *Francisco* · HECHO
- "Ver ruta" pasa por los puntos de encuentro de los pasajeros. La ven el conductor y los pasajeros del viaje; las reglas dejan leer `/encuentros/{viajeId}` solo a quienes participan.
- El orden de las paradas es el más corto: los puntos escritos se ubican en el mapa al guardarlos (`Servicios/Geocodificador.cs`, OpenStreetMap), se piden los tiempos de manejo entre todos los puntos a OSRM (gratuito) y se prueban todos los órdenes (`Servicios/OptimizadorDeRuta.cs`, con los métodos genéricos `Ordenes.Permutaciones<T>` y `Ordenes.Mejor<T>`). Si OSRM no responde, se usa la distancia en línea recta.

### HU25 (US E4-25) – Chat grupal del viaje · *Francisco*
**Como** conductor o pasajero, **quiero** un chat con todos los que viajan, **para** ponernos de acuerdo (por ejemplo, juntarnos en un mismo punto).

Criterios de aceptación:
- Cada viaje tiene un chat grupal: el conductor y los pasajeros anotados. Se abre desde Mis viajes con el botón "Chat", que muestra cuántos mensajes nuevos hay ("Chat (2)").
- Solo lo pueden leer y escribir quienes participan del viaje (regla en `/chats/{viajeId}`). Quien se baja deja de verlo.
- Mensajes automáticos: "Ana se sumó al viaje", "Ana se bajó del viaje", "Juan canceló el viaje".
- Con el chat cerrado, un mensaje nuevo dispara `MENSAJE_RECIBIDO` y aparece el aviso ("Ana en el chat de Saldán → UBP: …").
- La hora de cada mensaje la pone el servidor de Firebase, así el orden es el mismo para todos.
- Errores: mensaje vacío o de más de 500 caracteres (`ArgumentException`), no participa del viaje (`NoAnotadoException`), viaje terminado (`ViajeVencidoException`), sin internet (`SinConexionException`).

Prioridad: Should · Estimación: M

---

## 7. Definición de "Hecho"

- [ ] Compila sin errores y anda en Windows.
- [ ] Se probó con dos usuarios (una PC cada uno).
- [ ] Los errores se manejan con las excepciones del proyecto y quedan en el registro.
- [ ] Los eventos nuevos tienen su mensaje en `ConversorMensajes`.
- [ ] Pull request mergeado en `main` y tarjeta de Planner en "Hecho".

---

## 8. Para empezar con Claude

> Estoy trabajando en el proyecto Pool Compartido (.NET MAUI + Firebase). Leé `docs/Sprint4.md` (convenciones y estructura) y `docs/Sprint5.md` (qué cambió y las historias de este sprint). Voy a hacer la **HU14** en la rama `hu-14-octa`. Antes de escribir código, revisá los archivos que vas a tocar y contame qué pensás cambiar. Respetá las convenciones de excepciones, eventos y estilo, y no toques `ClavesFirebase.cs`.
