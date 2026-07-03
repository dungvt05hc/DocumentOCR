FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/DocumentOCR.WebApi/DocumentOCR.WebApi.csproj", "src/DocumentOCR.WebApi/"]
COPY ["src/DocumentOCR.Infrastructure/DocumentOCR.Infrastructure.csproj", "src/DocumentOCR.Infrastructure/"]
COPY ["src/DocumentOCR.Application/DocumentOCR.Application.csproj", "src/DocumentOCR.Application/"]
COPY ["src/DocumentOCR.Domain/DocumentOCR.Domain.csproj", "src/DocumentOCR.Domain/"]
RUN dotnet restore "src/DocumentOCR.WebApi/DocumentOCR.WebApi.csproj"

COPY . .
RUN dotnet publish "src/DocumentOCR.WebApi/DocumentOCR.WebApi.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DocumentOCR.WebApi.dll"]
