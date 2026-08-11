FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY playbook.sln .
COPY src/Playbook.Core/Playbook.Core.csproj src/Playbook.Core/
COPY src/Playbook.Application/Playbook.Application.csproj src/Playbook.Application/
COPY src/Playbook.Infrastructure/Playbook.Infrastructure.csproj src/Playbook.Infrastructure/
COPY src/Playbook.Web/Playbook.Web.csproj src/Playbook.Web/
RUN dotnet restore src/Playbook.Web/Playbook.Web.csproj

COPY src/Playbook.Core/ src/Playbook.Core/
COPY src/Playbook.Application/ src/Playbook.Application/
COPY src/Playbook.Infrastructure/ src/Playbook.Infrastructure/
COPY src/Playbook.Web/ src/Playbook.Web/

RUN dotnet publish src/Playbook.Web/Playbook.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Playbook.Web.dll"]
