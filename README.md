# Noxo Parental Control

MVP de contrôle parental Windows : agent .NET 8 + serveur Node.js/Express + SQLite + dashboard web.

## Fonctionnalités

- PIN parent
- limite quotidienne
- plage horaire autorisée
- liste de processus à bloquer
- journal des événements
- dashboard local
- agent Windows transparent
- mode simulation avant activation des blocages

## Installation

### Serveur

```powershell
cd Server
npm install
npm start
```

Dashboard : http://127.0.0.1:20570

PIN initial : `1234`

Change le PIN dès la première connexion.

### Agent

Installer .NET 8 SDK puis :

```powershell
cd Agent
dotnet run
```

Le mode par défaut est la simulation.

Pour activer les arrêts de processus configurés :

```powershell
$env:NOXO_ENFORCE="1"
dotnet run
```

Le MVP ne cherche pas à se dissimuler ni à contourner les protections Windows.
