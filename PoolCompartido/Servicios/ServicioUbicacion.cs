namespace PoolCompartido;

/// <summary>
/// HU23: obtiene la ubicación del dispositivo (GPS o WiFi) y el barrio donde está.
/// Las excepciones de MAUI se traducen a UbicacionNoDisponibleException con un mensaje claro.
/// </summary>
public static class ServicioUbicacion
{
    /// <summary>Cuánto se espera al GPS antes de rendirse.</summary>
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(15);

    /// <summary>Pide permiso (si hace falta), obtiene la posición y busca el barrio.</summary>
    /// <exception cref="UbicacionNoDisponibleException">Sin permiso, GPS apagado, sin GPS o no se encontró la posición.</exception>
    /// <exception cref="SinConexionException">No hay internet para buscar el barrio.</exception>
    /// <exception cref="ServidorException">OpenStreetMap respondió con un error.</exception>
    public static async Task<UbicacionActual> ObtenerAsync()
    {
        Location posicion = await ObtenerPosicionAsync();
        string barrio = await BuscadorDeBarrio.BuscarAsync(posicion.Latitude, posicion.Longitude);
        return new UbicacionActual(posicion.Latitude, posicion.Longitude, barrio);
    }

    private static async Task<Location> ObtenerPosicionAsync()
    {
        try
        {
            // En Android la primera vez aparece el cartel del sistema pidiendo permiso
            var permiso = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (permiso != PermissionStatus.Granted)
                throw new PermissionException("Permiso de ubicación denegado");

            var pedido = new GeolocationRequest(GeolocationAccuracy.Medium, EsperaMaxima);
            return await Geolocation.Default.GetLocationAsync(pedido)
                   ?? throw new UbicacionNoDisponibleException(
                          "No pudimos encontrar tu ubicación. Probá de nuevo o escribila a mano.");
        }
        // Las tres excepciones reales de MAUI, de la más específica a la más general
        catch (PermissionException ex)
        {
            throw new UbicacionNoDisponibleException(
                "No diste permiso para usar tu ubicación. Podés activarlo en la configuración del dispositivo.", ex);
        }
        catch (FeatureNotEnabledException ex)
        {
            throw new UbicacionNoDisponibleException("La ubicación del dispositivo está apagada. Activala y probá de nuevo.", ex);
        }
        catch (FeatureNotSupportedException ex)
        {
            throw new UbicacionNoDisponibleException("Este dispositivo no puede obtener la ubicación. Escribila a mano.", ex);
        }
    }
}
