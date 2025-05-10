# Server
The server by default
- runs on port 5146
- Uses Swagger UI in development mode (`/swagger/index.html`)
- Ignores API keys when not in development mode

# Installing the dependencies
## Ubuntu 24.04
1. `sudo apt update && sudo apt install dotnet-sdk-8.0 -y`
## Windows
Download the [.NET SDK](https://dotnet.microsoft.com/en-us/download) or follow these steps to use WSL:
1. Install Ubuntu in WSL (`wsl --install` and `wsl --install -d Ubuntu`)
2. Enter your WSL environment `wsl.exe` and configure it
3. Update via `sudo apt update && sudo apt upgrade -y && sudo snap refresh`
4. Continue here: [Ubuntu 24.04](#Ubuntu-24.04)

# MySQL database setup
1. Install the MySQL server:
- Linux/WSL: `sudo apt install mysql-server`
- Windows: [MySQL Community Server](https://dev.mysql.com/downloads/mysql/)
2. connect to it: `sudo mysql -u root` (Or from outside of WSL: `mysql -u root`)
3. Create the database
`CREATE DATABASE embeddingsearch; use embeddingsearch;`
4. Create the user
`CREATE USER 'embeddingsearch'@'%' identified by "somepassword!"; GRANT ALL ON embeddingsearch.* TO embeddingsearch; FLUSH PRIVILEGES;`
5. Create the tables using the CLI tool: `dotnet build` and `src/cli/bin/Debug/net8.0/cli -h $mysql_ip -p $mysql_port -U $mysql_username -P $mysql_password --database --setup`
