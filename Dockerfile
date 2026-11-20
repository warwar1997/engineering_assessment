FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# restore first so the layer is cached when only code changes
COPY Directory.Build.props Rezerv.Booking.sln ./
COPY src/Rezerv.Domain/Rezerv.Domain.csproj src/Rezerv.Domain/
COPY src/Rezerv.Application/Rezerv.Application.csproj src/Rezerv.Application/
COPY src/Rezerv.Infrastructure/Rezerv.Infrastructure.csproj src/Rezerv.Infrastructure/
COPY src/Rezerv.Api/Rezerv.Api.csproj src/Rezerv.Api/
RUN dotnet restore src/Rezerv.Api/Rezerv.Api.csproj

COPY src/ src/
RUN dotnet publish src/Rezerv.Api/Rezerv.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Rezerv.Api.dll"]
