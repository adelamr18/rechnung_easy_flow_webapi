FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files for restore layer caching
COPY ["src/InvoiceEasy.WebApi/InvoiceEasy.WebApi.csproj", "src/InvoiceEasy.WebApi/"]
COPY ["src/InvoiceEasy.Application/InvoiceEasy.Application.csproj", "src/InvoiceEasy.Application/"]
COPY ["src/InvoiceEasy.Infrastructure/InvoiceEasy.Infrastructure.csproj", "src/InvoiceEasy.Infrastructure/"]
COPY ["src/InvoiceEasy.Domain/InvoiceEasy.Domain.csproj", "src/InvoiceEasy.Domain/"]
COPY ["src/InvoiceEasy.sln", "src/"]

RUN dotnet restore "src/InvoiceEasy.WebApi/InvoiceEasy.WebApi.csproj"

# Copy the remaining source
COPY . .
WORKDIR /src/src/InvoiceEasy.WebApi

RUN dotnet publish "InvoiceEasy.WebApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Keep image slim; Railway/containers set PORT
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

COPY docker-entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["/entrypoint.sh"]
