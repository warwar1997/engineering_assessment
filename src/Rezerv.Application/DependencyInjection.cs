using Microsoft.Extensions.DependencyInjection;
using Rezerv.Application.Bookings;
using Rezerv.Application.Packages;
using Rezerv.Application.Timetable;
using Rezerv.Application.Users;
using Rezerv.Application.Waitlist;

namespace Rezerv.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IPackageService, PackageService>();
        services.AddScoped<ITimetableService, TimetableService>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<IWaitlistService, WaitlistService>();
        services.AddScoped<IDirectoryService, DirectoryService>();
        return services;
    }
}
