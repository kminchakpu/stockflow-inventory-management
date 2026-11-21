# CSE 325 Group Project
## Team Members
1. Kevin Cross Minchakpu
2. Derick Shanana
3. Essodong Guillaume NGnama
4. Enoh Uwem Akpan

# StockFlow – Inventory and Sales Management System
## Project Overview
StockFlow is a web-based inventory and sales management system designed for small retail operations.
The application provides a centralized platform for managing products, inventory, customers, employees, and sales activities in a streamlined operational workflow.

## Current Status
The project is on track and the core authentication flow is in place. The app currently includes:
- a branded login experience
- session-based authentication with role-aware claims
- admin and employee role enforcement
- protected admin and employee access screens
- a redirect flow for authenticated and unauthenticated users

## Project Objectives
- Develop a functional .NET Blazor web application.
- Implement secure user authentication.
- Implement Admin and Employee roles.
- Provide CRUD functionality.
- Automatically update inventory after successful sales.
- Prevent sales exceeding available inventory.
- Provide low-stock monitoring and sales history.
- Develop a responsive and accessible user interface.
- Test and deploy the application to a cloud service.

## Technology Stack
- C#
- .NET 10
- ASP.NET Core
- Blazor Server
- ASP.NET Core authorization and authentication
- HTML and CSS
- Bootstrap
- Git and GitHub
- Trello

## Main Features
- User authentication and authorization
- Admin and Employee dashboards
- Role-based access control
- Inventory overview
- Sales activity summaries
- Customer and operations panels
- Responsive dashboard layout
- Secure session-based sign in and sign out

## User Roles
1. Admin: Full access to administration and operational controls.
2. Employee: Access to employee-facing operations within the permitted workflow.

## Demo Test Credentials
Use these credentials for testing the app during development:

| Role | Email | Password |
| --- | --- | --- |
| Admin | admin@stockflow.local | Admin@123 |
| Employee | employee@stockflow.local | Employee@123 |

## Project Documentation
Additional documentation, installation steps, and user guides will be maintained in this repository as the application expands.

## Team Collaboration
1. GitHub is used for source-code management, branches, commits, and pull requests.
2. Trello is used to organize development tasks and monitor project progress.

## Project Status
In progress – authentication and role-based access have been implemented and are aligned with the current app design.

## Rules for team collaboration
### Before anyone starts coding, remember these rules:

| Activity | Team rule |
| --- | --- |
| Starting work | Pull the latest branch before editing |
| New feature | Create a feature branch |
| Saving changes | Commit regularly |
| Commit messages | Describe the actual change |
| Submitting work | Create a Pull Request |
| Code review | Another member reviews the changes |
| Merging | Merge approved work into the main branch |
| Final release | Merge tested code into main |

## Example Git commands:

### Get the latest changes
```bash
git pull origin main
```

### Create a feature branch
```bash
git checkout -b feature/products
```

### Stage your changes
```bash
git add .
```

### Commit your changes
```bash
git commit -m "Add product management functionality"
```

### Push your branch
```bash
git push -u origin feature/products
```
