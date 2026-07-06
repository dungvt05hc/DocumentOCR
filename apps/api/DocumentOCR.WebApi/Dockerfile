FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["apps/api/DocumentOCR.WebApi/DocumentOCR.WebApi.csproj", "apps/api/DocumentOCR.WebApi/"]
COPY ["apps/api/DocumentOCR.Infrastructure/DocumentOCR.Infrastructure.csproj", "apps/api/DocumentOCR.Infrastructure/"]
COPY ["apps/api/DocumentOCR.Application/DocumentOCR.Application.csproj", "apps/api/DocumentOCR.Application/"]
COPY ["apps/api/DocumentOCR.Domain/DocumentOCR.Domain.csproj", "apps/api/DocumentOCR.Domain/"]
RUN dotnet restore "apps/api/DocumentOCR.WebApi/DocumentOCR.WebApi.csproj"

COPY . .
RUN dotnet publish "apps/api/DocumentOCR.WebApi/DocumentOCR.WebApi.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DocumentOCR.WebApi.dll"]
