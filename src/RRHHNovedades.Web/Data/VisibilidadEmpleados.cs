namespace RRHHNovedades.Web.Data;

/// <summary>
/// Estado en memoria de la configuración "mostrar empleados bloqueados" (persistida en
/// <see cref="Models.ConfiguracionParte.MostrarBloqueados"/>). Lo lee el filtro global de
/// <see cref="AppDbContext"/> en cada consulta; se carga al arrancar, lo actualiza Configuración al
/// guardar y el scheduler lo refresca en cada tick (por si hubiera más de una réplica).
/// </summary>
public sealed class VisibilidadEmpleados
{
    private volatile bool _mostrarBloqueados;

    public bool MostrarBloqueados
    {
        get => _mostrarBloqueados;
        set => _mostrarBloqueados = value;
    }
}
