using System;

namespace GaltekClassroom.Bootstrapper
{
    public sealed class Presentation
    {
        public string Eyebrow { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public string CardTitle { get; set; }
        public string CardBody { get; set; }
        public string Primary { get; set; }
        public string Secondary { get; set; }
        public string Destructive { get; set; }
        public bool PrimaryEnabled { get; set; }
        public bool ShowProgress { get; set; }
        public bool ShowLog { get; set; }
        public bool ShowRestart { get; set; }
    }

    public static class PresentationResolver
    {
        public static Presentation Resolve(ProductState state, InstallerIntent intent, string installed, string available, string component, string reason, string errorCode, string stage, bool needsPredecessorCleanup = false)
        {
            switch (state)
            {
                case ProductState.Boot:
                case ProductState.Detecting:
                    return P("REVISANDO ESTE EQUIPO", "Preparando Galtek Classroom", "Estamos comprobando la instalación de este equipo.", "Detección en curso", "Esta revisión es de solo lectura y no requiere permisos de administrador.", null, "Cancelar", null, false);
                case ProductState.Fresh:
                    return P("LISTO PARA INSTALAR", "Instalar Galtek Classroom", "Prepara este equipo para trabajar con Galtek Classroom.", "Este equipo está listo", "Se instalarán el servicio, el control de sesión y la integración con Windows.", "Instalar", "Cancelar", null, true);
                case ProductState.LegacySupported:
                    return P("INSTALACIÓN ANTERIOR DETECTADA", "Actualizar Galtek Classroom", "Se detectó una instalación anterior de Galtek Classroom.", "Tus datos permanecen en el equipo", "Se conservarán la configuración, la identidad del equipo y los datos existentes.", "Actualizar", "Cancelar", null, true);
                case ProductState.InstalledSame:
                    if (needsPredecessorCleanup)
                        return P("MANTENIMIENTO PENDIENTE", "La actualización está instalada", "Galtek Classroom " + Safe(available) + " ya está instalado. Falta retirar el registro de una versión anterior.", "El producto actual está listo", "Esta operación solo completará el mantenimiento pendiente; no eliminará datos del equipo.", "Completar actualización", "Cancelar", null, true);
                    return P("VERSIÓN " + Safe(installed), "Galtek Classroom ya está instalado", "Puedes reparar los componentes instalados o quitar el cliente.", "Instalación registrada", "La reparación vuelve a comprobar todos los componentes sin eliminar los datos del equipo.", "Reparar instalación", "Cancelar", "Desinstalar", true);
                case ProductState.InstalledOlder:
                    return P("NUEVA VERSIÓN DISPONIBLE", "Actualizar Galtek Classroom", "Hay una versión más reciente lista para instalar.", "Instalada: " + Safe(installed), "Disponible: " + Safe(available), "Actualizar", "Cancelar", null, true);
                case ProductState.InstalledNewer:
                    return P("ACTUALIZACIÓN BLOQUEADA", "Ya tienes una versión más reciente", "Este instalador no puede reemplazar una versión más reciente de Galtek Classroom.", "Instalada: " + Safe(installed), "Disponible en este instalador: " + Safe(available), null, "Cerrar", null, false);
                case ProductState.RepairablePartial:
                    return P("INSTALACIÓN INCOMPLETA", "Reparar Galtek Classroom", "Detectamos una instalación incompleta y podemos repararla sin eliminar los datos del equipo.", "Componentes Galtek detectados", "La instalación volverá a converger los componentes conocidos de forma segura.", "Reparar instalación", "Cancelar", null, true);
                case ProductState.BlockedConflict:
                    return P("REVISIÓN NECESARIA", "No podemos continuar de forma segura", "Detectamos una instalación que no coincide con una configuración válida de Galtek Classroom.", "Componente: " + Safe(component), "Motivo: " + Safe(reason), null, "Cerrar", null, false, true);
                case ProductState.DetectionFailed:
                    return P("NO SE PUDO VERIFICAR", "No pudimos verificar este equipo", "Para evitar modificar una instalación existente de forma insegura, Galtek Classroom no continuará.", "Detección detenida", "Motivo: " + Safe(reason), null, "Cerrar", null, false, true);
                case ProductState.Planning:
                    return Progress("Preparando la operación", "Burn está calculando los cambios necesarios.", stage);
                case ProductState.ApplyingInstall:
                    return Progress("Instalando Galtek Classroom", "Esto puede tardar algunos minutos en equipos con disco duro.", stage);
                case ProductState.ApplyingUpdate:
                    return Progress("Actualizando Galtek Classroom", "Tus datos y configuración se conservarán.", stage);
                case ProductState.ApplyingRepair:
                    return Progress("Reparando Galtek Classroom", "Estamos comprobando y restaurando los componentes instalados.", stage);
                case ProductState.ApplyingUninstall:
                    return Progress("Desinstalando Galtek Classroom", "Los datos locales del producto se conservarán.", stage);
                case ProductState.Success:
                    return Success(intent);
                case ProductState.CleanupPending:
                    return P("MANTENIMIENTO PENDIENTE", "La actualización está instalada", "Galtek Classroom funciona con la versión actual, pero no fue posible retirar por completo el registro anterior.", "No se revirtió el producto", "Vuelve a ejecutar este instalador para completar el mantenimiento pendiente.", null, "Cerrar", null, false, true);
                case ProductState.RestartRequired:
                    return P("OPERACIÓN COMPLETADA", "Se necesita reiniciar Windows", "Reinicia el equipo para completar la instalación de Galtek Classroom.", "El reinicio no es automático", "Puedes reiniciar ahora o hacerlo más tarde.", "Reiniciar ahora", "Más tarde", null, true, false, false, true);
                case ProductState.Failure:
                    return P("NO SE COMPLETÓ", "No pudimos completar la instalación", "Galtek Classroom no pudo terminar de configurar este equipo.", "Código: " + Safe(errorCode), "Windows intentó revertir los cambios de esta operación.", null, "Cerrar", null, false, true);
                default:
                    throw new ArgumentOutOfRangeException(nameof(state));
            }
        }

        private static Presentation Progress(string title, string body, string stage)
        {
            var p = P("OPERACIÓN EN CURSO", title, body, stage ?? "Preparando el equipo", "No cierres esta ventana mientras se aplican los cambios.", null, null, null, false);
            p.ShowProgress = true;
            return p;
        }

        private static Presentation Success(InstallerIntent intent)
        {
            switch (intent)
            {
                case InstallerIntent.CompleteUpdate:
                    return P("OPERACIÓN COMPLETADA", "La actualización quedó completa", "Se retiró el registro pendiente de la versión anterior.", "Equipo preparado", "Galtek Classroom permanece instalado y listo.", "Finalizar", null, null, true);
                case InstallerIntent.Update:
                    return P("OPERACIÓN COMPLETADA", "Galtek Classroom se actualizó correctamente", "La instalación anterior quedó actualizada y sus datos se conservaron.", "Equipo preparado", "Ya puedes cerrar el instalador.", "Finalizar", null, null, true);
                case InstallerIntent.Repair:
                    return P("OPERACIÓN COMPLETADA", "La instalación fue reparada", "Los componentes de Galtek Classroom quedaron configurados nuevamente.", "Reparación completa", "Los datos del equipo se conservaron.", "Finalizar", null, null, true);
                case InstallerIntent.Uninstall:
                    return P("OPERACIÓN COMPLETADA", "Galtek Classroom fue desinstalado", "Los componentes se quitaron de este equipo.", "Datos preservados", "Los datos locales del producto se conservaron.", "Finalizar", null, null, true);
                default:
                    return P("OPERACIÓN COMPLETADA", "Galtek Classroom está listo", "Este equipo quedó preparado para trabajar con Galtek Classroom.", "Instalación completa", "Ya puedes cerrar el instalador.", "Finalizar", null, null, true);
            }
        }

        private static Presentation P(string eyebrow, string title, string body, string cardTitle, string cardBody, string primary, string secondary, string destructive, bool enabled, bool log = false, bool progress = false, bool restart = false)
        {
            return new Presentation { Eyebrow = eyebrow, Title = title, Body = body, CardTitle = cardTitle, CardBody = cardBody, Primary = primary, Secondary = secondary, Destructive = destructive, PrimaryEnabled = enabled, ShowLog = log, ShowProgress = progress, ShowRestart = restart };
        }

        private static string Safe(string value) => String.IsNullOrWhiteSpace(value) ? "No disponible" : value;
    }
}
