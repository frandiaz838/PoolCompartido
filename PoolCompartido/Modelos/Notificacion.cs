using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PoolCompartido;

/// <summary>
/// HU26: un aviso del centro de notificaciones ("Ana se anotó a tu viaje a UBP").
/// Notifica sus cambios (leída, texto) para que la lista se actualice sola.
/// </summary>
public class Notificacion : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Viaje al que se refiere (null en avisos generales, como "Sin conexión").</summary>
    public string? ViajeId { get; init; }

    /// <summary>Evento que lo originó (null en avisos generales).</summary>
    public ViajeEvento? Evento { get; init; }

    public DateTime Hora { get; init; } = DateTime.Now;

    /// <summary>false en los avisos que solo se muestran en el cartel y no quedan en el centro.</summary>
    public bool Guardada { get; init; } = true;

    private string _texto = "";
    public string Texto
    {
        get => _texto;
        set { if (_texto != value) { _texto = value; Avisar(); } }
    }

    private bool _leida;
    public bool Leida
    {
        get => _leida;
        set
        {
            if (_leida == value) return;
            _leida = value;
            Avisar();
            Avisar(nameof(NoLeida));
        }
    }

    public bool NoLeida => !Leida;

    /// <summary>Ícono según el tipo de aviso (siempre acompaña al texto, nunca lo reemplaza).</summary>
    public string Icono => Evento switch
    {
        ViajeEvento.VIAJE_PUBLICADO => "🚗",
        ViajeEvento.ASIENTO_RESERVADO => "🙋",
        ViajeEvento.ASIENTO_LIBERADO => "👋",
        ViajeEvento.VIAJE_CANCELADO => "⛔",
        ViajeEvento.REEMPLAZO_ENCONTRADO => "🔁",
        ViajeEvento.SIN_REEMPLAZO => "⚠️",
        ViajeEvento.COSTO_RECALCULADO => "💲",
        ViajeEvento.VIAJE_INICIADO => "🛣️",
        ViajeEvento.VIAJE_FINALIZADO => "🏁",
        ViajeEvento.PASAJERO_A_BORDO => "✅",
        ViajeEvento.MENSAJE_RECIBIDO => "💬",
        ViajeEvento.CALIFICACION_RECIBIDA => "⭐",   // HU31
        ViajeEvento.NO_SHOW_DETECTADO => "⏱️",
        _ => "🔔"
    };

    /// <summary>"Recién", "Hace 5 min", "14:32", "Ayer 14:32" o "27/10 14:32".</summary>
    public string HoraTexto => HoraTextoEn(DateTime.Now);

    /// <summary>Separado de DateTime.Now para poder probarlo.</summary>
    public string HoraTextoEn(DateTime ahora)
    {
        var pasaron = ahora - Hora;
        if (pasaron < TimeSpan.FromMinutes(1)) return "Recién";
        if (pasaron < TimeSpan.FromHours(1)) return $"Hace {(int)pasaron.TotalMinutes} min";
        if (Hora.Date == ahora.Date) return Hora.ToString("HH:mm");
        if (Hora.Date == ahora.Date.AddDays(-1)) return $"Ayer {Hora:HH:mm}";
        return Hora.ToString("dd/MM HH:mm");
    }

    /// <summary>Para refrescar "Hace 5 min" cuando se vuelve a abrir el centro.</summary>
    public void RefrescarHora() => Avisar(nameof(HoraTexto));

    private void Avisar([CallerMemberName] string? propiedad = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
}
