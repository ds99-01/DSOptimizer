using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;
using DSOptimizer.Windows.Native;

namespace DSOptimizer.Windows;

/// <summary>Usługi Windows: odczyt z rejestru + ServiceController, zmiana typu startu przez ChangeServiceConfig.</summary>
public sealed class WindowsServiceManager : IServiceManager
{
    private const string ServicesPath = @"HKLM\SYSTEM\CurrentControlSet\Services\";

    private readonly IRegistry _registry;

    public WindowsServiceManager(IRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public ServiceQueryResult Query(string name)
    {
        bool running;
        try
        {
            using var controller = new ServiceController(name);
            running = controller.Status == ServiceControllerStatus.Running;
        }
        catch (InvalidOperationException ex)
        {
            return ErrorCode(ex) switch
            {
                NativeMethods.ErrorServiceDoesNotExist => ServiceQueryResult.NotFound(),
                NativeMethods.ErrorAccessDenied => ServiceQueryResult.AccessDenied(ex.Message),
                _ => ServiceQueryResult.Failed(ex.Message),
            };
        }

        var start = _registry.Read(ServicesPath + name, "Start");
        if (start.Status == ReadStatus.AccessDenied)
        {
            return ServiceQueryResult.AccessDenied(start.Error ?? "Brak dostępu do konfiguracji usługi.");
        }

        if (start.Value.Data is not uint startValue)
        {
            return ServiceQueryResult.Failed($"Brak wartości Start dla usługi {name}.");
        }

        var delayed = _registry.Read(ServicesPath + name, "DelayedAutostart").Value.Data is uint flag && flag == 1;
        ServiceStartType? startType = startValue switch
        {
            0 => ServiceStartType.Boot,
            1 => ServiceStartType.System,
            2 => delayed ? ServiceStartType.AutomaticDelayed : ServiceStartType.Automatic,
            3 => ServiceStartType.Manual,
            4 => ServiceStartType.Disabled,
            _ => null,
        };

        return startType is { } type
            ? ServiceQueryResult.Found(type, running)
            : ServiceQueryResult.Failed(string.Format(CultureInfo.InvariantCulture, "Nieznany typ startu {0} usługi {1}.", startValue, name));
    }

    public void SetStartType(string name, ServiceStartType startType)
    {
        uint native = startType switch
        {
            ServiceStartType.Automatic or ServiceStartType.AutomaticDelayed => 2u,
            ServiceStartType.Manual => 3u,
            ServiceStartType.Disabled => 4u,
            _ => throw new OperationException(OperationErrorKind.Unexpected, $"Ustawianie typu startu {startType} nie jest obsługiwane."),
        };

        using var service = OpenServiceHandle(name, NativeMethods.ServiceChangeConfig | NativeMethods.ServiceQueryConfig);

        if (!NativeMethods.ChangeServiceConfig(service, NativeMethods.ServiceNoChange, native, NativeMethods.ServiceNoChange, null, null, IntPtr.Zero, null, null, null, null))
        {
            throw FromLastError(name);
        }

        if (native == 2)
        {
            var info = new NativeMethods.ServiceDelayedAutoStartInfo { DelayedAutostart = startType == ServiceStartType.AutomaticDelayed };
            if (!NativeMethods.ChangeServiceConfig2(service, NativeMethods.ServiceConfigDelayedAutoStartInfo, ref info))
            {
                throw FromLastError(name);
            }
        }
    }

    public void StopService(string name, TimeSpan timeout)
    {
        using (var service = OpenServiceHandle(name, NativeMethods.ServiceStop | NativeMethods.ServiceQueryStatus))
        {
            var status = default(NativeMethods.ServiceStatus);
            if (!NativeMethods.ControlService(service, NativeMethods.ServiceControlStop, ref status))
            {
                var error = Marshal.GetLastWin32Error();
                if (MapStopError(name, error) is { } failure)
                {
                    throw failure;
                }

                return;
            }
        }

        Control(name, controller => controller.WaitForStatus(ServiceControllerStatus.Stopped, timeout));
    }

    public void StartService(string name, TimeSpan timeout) => Control(name, controller =>
    {
        if (controller.Status != ServiceControllerStatus.Running)
        {
            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, timeout);
        }
    });

    /// <summary>
    /// <see cref="ServiceController.DependentServices"/> = <c>EnumDependentServices</c>: zależne bezpośrednie i pośrednie (także sterowniki),
    /// w kolejności odwrotnej do startu, czyli kolejności zatrzymywania. „Działające” = każdy stan inny niż Stopped.
    /// </summary>
    public IReadOnlyList<string> GetRunningDependents(string name)
    {
        var running = new List<string>();
        Control(name, controller =>
        {
            foreach (var dependent in controller.DependentServices)
            {
                using (dependent)
                {
                    if (dependent.Status != ServiceControllerStatus.Stopped)
                    {
                        running.Add(dependent.ServiceName);
                    }
                }
            }
        });
        return running;
    }

    /// <summary>
    /// <see cref="ServiceController.Stop"/> najpierw zatrzymuje działające zależne (każdą czeka do 30 s), potem samą usługę – ta sama droga,
    /// którą zmierzono na VM 26H2 dla <c>cryptsvc</c> (z <c>AppIDSvc</c> i sterownikiem <c>applockerfltr</c>, ok. 0,5 s).
    /// </summary>
    public void StopServiceWithDependents(string name, TimeSpan timeout) => Control(name, controller =>
    {
        if (controller.Status != ServiceControllerStatus.Stopped)
        {
            controller.Stop();
            controller.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
        }
    });

    /// <summary>Mapuje kod Win32 z ControlService(STOP) na wyjątek; 1062 (już zatrzymana) nie jest błędem, więc zwraca null.</summary>
    internal static OperationException? MapStopError(string name, int error)
    {
        if (error == NativeMethods.ErrorServiceNotActive)
        {
            return null;
        }

        if (error == NativeMethods.ErrorDependentServicesRunning)
        {
            return new OperationException(OperationErrorKind.Unexpected, $"Usługa {name} ma działające usługi zależne – nie zatrzymuję jej, żeby zmiana pozostała odwracalna.");
        }

        return FromError(name, error);
    }

    private static SafeServiceHandle OpenServiceHandle(string name, uint access)
    {
        using var manager = NativeMethods.OpenSCManager(null, null, NativeMethods.ScManagerConnect);
        if (manager.IsInvalid)
        {
            throw FromLastError(name);
        }

        var service = NativeMethods.OpenService(manager, name, access);
        if (service.IsInvalid)
        {
            var failure = FromLastError(name);
            service.Dispose();
            throw failure;
        }

        return service;
    }

    private static void Control(string name, Action<ServiceController> action)
    {
        try
        {
            using var controller = new ServiceController(name);
            action(controller);
        }
        catch (System.ServiceProcess.TimeoutException ex)
        {
            throw new OperationException(OperationErrorKind.Timeout, $"Usługa {name} nie zmieniła stanu w wymaganym czasie.", ex);
        }
        catch (InvalidOperationException ex)
        {
            var kind = ErrorCode(ex) switch
            {
                NativeMethods.ErrorServiceDoesNotExist => OperationErrorKind.NotFound,
                NativeMethods.ErrorAccessDenied => OperationErrorKind.AccessDenied,
                _ => OperationErrorKind.Unexpected,
            };
            throw new OperationException(kind, $"Usługa {name}: {ex.Message}", ex);
        }
    }

    private static int? ErrorCode(InvalidOperationException ex) => (ex.InnerException as Win32Exception)?.NativeErrorCode;

    private static OperationException FromLastError(string name) => FromError(name, Marshal.GetLastWin32Error());

    private static OperationException FromError(string name, int error)
    {
        var kind = error switch
        {
            NativeMethods.ErrorAccessDenied => OperationErrorKind.AccessDenied,
            NativeMethods.ErrorServiceDoesNotExist => OperationErrorKind.NotFound,
            _ => OperationErrorKind.Unexpected,
        };
        return new OperationException(kind, string.Format(CultureInfo.InvariantCulture, "Usługa {0}: {1} (kod {2}).", name, new Win32Exception(error).Message, error));
    }
}
