# Sprint 4 – Pool Compartido

Guía del sprint para el equipo (Díaz y Brachetti). También está pensada para pasársela a Claude como contexto: tiene todo lo necesario para entender el proyecto y trabajar sin romper lo que ya anda.

---

## 1. Objetivo del sprint

Completar el núcleo del proyecto: salir de un viaje, búsqueda automática de reemplazo con eventos encadenados, mensajes según el rol, ver la ruta en el mapa y borrar la cuenta.

| HU | Qué es | Quién | Orden |
|---|---|---|---|
| HU22 | Ver la ruta en Google Maps | Compañero | 1 |
| HU7 | Mensajes adaptados al rol (conversión de mensajes) | Compañero | 2 |
| HU4 | Salirse de un viaje | Francisco | 1 |
| HU21 | Borrar mi cuenta (depende de HU4) | Francisco | 2 |
| HU6 + HU11 | Búsqueda automática de reemplazo + encadenamiento de eventos | Los dos juntos | 3 (al final) |

---

## 2. Cómo trabajamos sin pisarnos

1. **Nunca trabajar directo en `main`.** Una rama por HU: `hu22-ver-ruta`, `hu7-mensajes`, `hu4-salirse`, `hu21-borrar-cuenta`, `hu6-reemplazo`.
2. **Antes de crear la rama:** estar en `main` y hacer Git → "Pull".
3. **Commits chicos y seguidos**, con mensaje que empiece con la HU: `HU22: botón Ver ruta`.
4. **Al terminar:** Enviar cambios → en GitHub, *Compare & pull request* → el otro lo revisa y hace *Merge*.
5. **Después de cada merge,** los dos vuelven a `main` y hacen "Pull". Si tenés una rama abierta, traé los cambios de `main` a tu rama (Git → Administrar ramas → combinar `main` en tu rama).
6. **Archivos compartidos: avisar por WhatsApp antes de tocarlos.**
   - `Resources/Styles/Styles.xaml`, `AppShell.xaml`, `MauiProgram.cs`, `Modelos/Viaje.cs`, `Eventos/ViajeEvento.cs`.
   - **Reglas de Firebase:** las cambia uno solo. Primero se actualiza `database.rules.json` (raíz del repo) y después se pegan en la consola de Firebase → Realtime Database → Reglas → Publicar.
7. **`Servicios/ClavesFirebase.cs` no está en GitHub** (está en `.gitignore`). Cada uno tiene su copia local. No borrarlo ni subirlo.
8. **Planner:** cada uno mueve sus tarjetas a "En curso" y a "Hecho". Un mensaje por día con qué hice, qué voy a hacer y si estoy trabado.

---

## 3. Contexto técnico del proyecto

### Stack
- **C# con .NET MAUI (.NET 10)**: un solo proyecto que corre en Windows y Android. iOS no, porque no tenemos Mac.
- **Firebase**: Authentication (login con mail y contraseña) y Realtime Database (viajes compartidos), usados por su **API REST** con `HttpClient`, sin SDK.
- Conceptos de la materia que el proyecto tiene que mostrar: **eventos y callbacks, bucle despachador con tabla de eventos, evento Paint (redibujar), conversión de mensajes, programación genérica y excepciones**.

### Estructura
```
PoolCompartido/
├── Modelos/        Viaje (INotifyPropertyChanged), Usuario (record)
├── Eventos/        Despachador<TEvento,TDatos> (genérico), ViajeEvento, EventoSesion
├── Excepciones/    ViajeExcepciones, ExcepcionesAutenticacion, ExcepcionesRemotas
├── Servicios/      ServicioViajes, ServicioAutenticacion, RepositorioFirebase,
│                   RepositorioViajes (caché local), DatosFirebase (DTOs + ConversorViajes),
│                   ClienteHttp, RegistroErrores, Configuracion, ClavesFirebase (no está en git)
├── Controles/      EntryConIcono
├── LoginPage, MainPage (Viajes disponibles), MisViajesPage, PublicarViajePage,
│   PerfilPage, RegistroErroresPage
└── App.xaml.cs     navegación login ↔ app y timer de sincronización
```

### Cómo funciona por dentro
- **`ServicioViajes.Instancia`** (singleton) tiene toda la lógica: `PublicarAsync`, `AnotarseAsync`, `CancelarViajeAsync`, `SincronizarAsync`, `MisViajes()`. No sabe nada de pantallas: valida, lanza excepciones y dispara eventos.
- **`Despachador<TEvento, TDatos>`** es el bucle despachador: un `Dictionary<TEvento, List<Action<TDatos>>>` más una cola. `Registrar(evento, callback)` y `Disparar(evento, datos)`. Si una callback dispara otro evento, se encola y se procesa después, en orden: eso es el **encadenamiento (HU11)**.
- **Hay dos despachadores**: `ServicioViajes.Instancia.Eventos` (`Despachador<ViajeEvento, Viaje>`) y `ServicioAutenticacion.Instancia.Eventos` (`Despachador<EventoSesion, Usuario>`).
- **Las pantallas registran callbacks en su constructor** (por ejemplo, `MainPage` registra `VIAJE_PUBLICADO`, `ASIENTO_RESERVADO` y `VIAJE_CANCELADO`) y llaman a `Redibujar()`, que es el equivalente al evento Paint.
- **Sincronización:** `App` tiene un timer que cada 5 segundos llama a `SincronizarAsync()`. Compara con Firebase y **dispara los mismos eventos** que si el cambio se hubiera hecho en este dispositivo. Por eso, cuando otra persona se anota a tu viaje, te llega el aviso.
- **Concurrencia:** para modificar un viaje existente se lee con **ETag** (`ObtenerConEtagAsync`) y se guarda con `ReemplazarSiNoCambioAsync`. Si otro lo cambió en el medio (412), se reintenta hasta 3 veces y después se lanza `ConflictoException`. **Toda operación nueva que modifique un viaje tiene que seguir este patrón** (ver `AnotarseAsync`).
- **Datos en Firebase:** `/viajes/{id}` con `origen, destino, fecha (yyyy-MM-dd), hora (HH:mm), conductorUid, conductorNombre, comentario, asientosDisponibles, cancelado, pasajeros { uid: nombre }`.
- **Usuario actual:** `ServicioAutenticacion.Instancia.UsuarioActual` (Uid, Nombre, Email). En `Viaje` ya existen `EsDelUsuarioActual`, `EstoyAnotado`, `RolUsuarioActual`, `PuedeAnotarse` y `PuedeCancelar` para usar en los bindings.

### Excepciones (HU12): convenciones
- Los errores de negocio heredan de **`ViajeException : InvalidOperationException`** (`ViajeCompletoException`, `AutoReservaException`, `YaAnotadoException`, `ViajeVencidoException`, `ViajeCanceladoException`, `NoEsConductorException`, `ConflictoException`).
- Red y servidor: `SinConexionException`, `PermisoDenegadoException`, `ServidorException`. Login: `AutenticacionException` y derivadas (`CredencialesInvalidasException`, `UsuarioExistenteException`, `SesionVencidaException`).
- Datos inválidos del usuario: `ArgumentException` / `ArgumentOutOfRangeException` (con `innerException: null` para que el mensaje quede limpio).
- **En las pantallas**: `catch` de la más específica a la más general, **nunca `catch (Exception)` suelto**. Los errores del usuario se registran con `RegistroErrores.RegistrarAviso(ex, "dónde")` y las fallas del sistema con `RegistrarError`. Para el grupo de red se usa `catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)`.
- Mientras se espera al servidor, el botón se deshabilita (`IsEnabled = false`) y se rehabilita en `finally`.

### Estilo de código
- **Todo en español** (clases, métodos, variables, mensajes), con comentarios que expliquen el *por qué* y qué concepto de la materia se usa.
- Mensajes al usuario en **español rioplatense** ("Publicaste", "Anotate", "Probá de nuevo").
- Cada HU tiene que **compilar y probarse en Windows** (y si se puede, en el emulador de Android) antes del pull request.

---

## 4. Historias del sprint

### HU22 (US E1-22) – Ver la ruta en Google Maps · *Compañero*
**Como** pasajero, **quiero** ver el recorrido del viaje en Google Maps, **para** saber si me queda cómodo.

Criterios de aceptación:
- Cada viaje tiene un botón "Ver ruta", en *Viajes disponibles* y en *Mis viajes*.
- Abre Google Maps (la app o el navegador) con origen y destino.
- Si no se puede abrir, se muestra un aviso en vez de cerrarse la app, y queda registrado.

Pistas técnicas:
- No hace falta clave de API: se arma una URL de Google Maps y se abre con `Launcher.Default.OpenAsync(uri)`:
  `https://www.google.com/maps/dir/?api=1&origin={origen}&destination={destino}&travelmode=driving`
  Hay que escapar los textos con `Uri.EscapeDataString`. Conviene agregar ", Argentina" para que "Palermo" no termine en Italia.
- Poner la lógica en una clase chica (por ejemplo `Servicios/ServicioMapas.cs`, con un método que arme la URL y otro que la abra), no en la pantalla.
- El botón va en el `DataTemplate` de `MainPage.xaml` y de `MisViajesPage.xaml`. Ojo: Francisco también toca `MisViajesPage.xaml` en la HU4 (agrega un botón "Salirme"). Son líneas distintas, así que Git las combina solo, pero avisense.

Prioridad: Could · Estimación: S

### HU7 (US E4-07) – Mensajes adaptados al rol · *Compañero*
**Como** usuario, **quiero** recibir un mensaje claro y adaptado a mi rol cada vez que cambia el estado de un viaje en el que participo, **para** estar al tanto sin revisar manualmente.

Criterios de aceptación:
- Cada evento relevante (`VIAJE_PUBLICADO`, `ASIENTO_RESERVADO`, `ASIENTO_LIBERADO`, `VIAJE_CANCELADO` y, cuando exista la HU6, `REEMPLAZO_ENCONTRADO` y `SIN_REEMPLAZO`) produce un mensaje distinto según si soy **conductor**, **pasajero** o **ninguno**.
- El mensaje se muestra apenas ocurre el evento (en el aviso de abajo de `MainPage`).
- Si el evento no me involucra, no se muestra nada, salvo `VIAJE_PUBLICADO` de otros ("Nuevo viaje de…").

Pistas técnicas:
- Es el tema **"conversión de mensajes"** de la materia: crear `Servicios/ConversorMensajes.cs` con un método tipo `string? Convertir(ViajeEvento evento, Viaje viaje)` que use un `switch` sobre el evento y el rol, y devuelva el texto o `null` si no corresponde avisar.
- Hoy los mensajes están escritos directamente en las callbacks del constructor de `MainPage.xaml.cs`. La idea es reemplazarlos por una única forma: `MostrarAvisoSiCorresponde(ConversorMensajes.Convertir(evento, viaje))`.
- `ASIENTO_LIBERADO` lo dispara la HU4 de Francisco. Se puede dejar el caso escrito aunque todavía no se dispare.

Prioridad: Must · Estimación: M

### HU4 (US E2-04) – Salirse de un viaje · *Francisco*
**Como** pasajero, **quiero** cancelar mi participación en un viaje, **para** liberar el lugar si no puedo ir.

Criterios de aceptación:
- En *Mis viajes*, en los viajes donde soy pasajero, hay un botón "Salirme" (con confirmación).
- Se libera el asiento y se dispara `ASIENTO_LIBERADO`; el conductor recibe el aviso.
- No dispara búsqueda de reemplazo (eso es solo si cancela el conductor).
- Usa el patrón con ETag; las reglas de Firebase se actualizan para permitir que un pasajero se quite a sí mismo y sume un asiento.

Prioridad: Should · Estimación: S

### HU21 (US E10-21) – Borrar mi cuenta · *Francisco*
**Como** usuario, **quiero** borrar mi cuenta desde la app, **para** que no queden mis datos si dejo de usarla.

Criterios de aceptación:
- En Mi perfil hay un botón "Borrar cuenta" que pide la contraseña para confirmar.
- Se cancelan mis viajes activos como conductor (avisa a los pasajeros) y me salgo de los viajes donde estoy anotado (HU4).
- Se borra la cuenta en Firebase y los datos del dispositivo, y la app vuelve al login.

Prioridad: Should · Estimación: M · Depende de: HU4

### HU6 + HU11 – Búsqueda automática de reemplazo · *Los dos juntos, al final*
**Como** pasajero afectado por una cancelación, **quiero** que el sistema busque automáticamente otro viaje con ruta y horario similares, **para** no quedarme sin viaje.

Criterios de aceptación:
- Al recibir `VIAJE_CANCELADO` se ejecuta la callback `BuscarReemplazo`.
- Un viaje sirve de reemplazo si está activo, tiene el mismo destino, es la misma fecha, sale con **±1 hora** de diferencia y tiene asientos libres.
- Si encuentra, dispara `REEMPLAZO_ENCONTRADO` y avisa con el viaje sugerido; si no, dispara `SIN_REEMPLAZO` ("Buscá otra opción").
- Los eventos se encadenan a través del despachador: `VIAJE_CANCELADO → REEMPLAZO_ENCONTRADO / SIN_REEMPLAZO` (HU11).

Lo hacemos juntos porque toca el corazón del servicio y es lo que más nos van a preguntar en la defensa.

Prioridad: Must · Estimación: L

---

## 5. Definición de "Hecho"

Una HU está hecha cuando:
- [ ] Compila sin errores y anda en Windows (y si se puede, en el emulador).
- [ ] Se probó con **dos usuarios** cuando corresponde (una compu cada uno, o compu + emulador).
- [ ] Los errores se manejan con las excepciones del proyecto y quedan en el registro.
- [ ] El pull request fue revisado por el otro y está mergeado en `main`.
- [ ] La tarjeta de Planner está en "Hecho".

---

## 6. Para empezar con Claude

Prompt sugerido para arrancar una HU:

> Estoy trabajando en el proyecto Pool Compartido (.NET MAUI + Firebase). Leé `docs/Sprint4.md` del repo, que tiene el contexto técnico y las convenciones. Voy a hacer la **HU22** en la rama `hu22-ver-ruta`. Antes de escribir código, revisá los archivos que vas a tocar y contame qué pensás cambiar. Respetá las convenciones de excepciones, eventos y estilo del documento, y no toques `ClavesFirebase.cs`.
