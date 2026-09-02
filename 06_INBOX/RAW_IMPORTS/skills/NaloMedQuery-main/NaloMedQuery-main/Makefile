APP_NAME=medquery
MYSQL_CONTAINER=medquery_mysql
DB_NAME=medquery
DB_USER=medquery_user
DB_PASS=medquery_pass
BACKUP=medquery_backup.sql

.PHONY: help build run test up down logs db-backup db-restore db-shell clean

help:
	@echo "Comandos disponíveis:"
	@echo " make i            - instala depências"
	@echo " make build        - build do projeto"
	@echo " make run          - roda a aplicação"
	@echo " make test         - executa testes"
	@echo " make up           - sobe docker"
	@echo " make down         - derruba docker"
	@echo " make logs         - logs do mysql"
	@echo " make db-backup    - backup do banco"
	@echo " make db-restore   - restore do banco"
	@echo " make db-shell     - entra no mysql"
	@echo " make clean        - limpa target"

i:
	mvn clean install

build:
	mvn clean package -DskipTests

run:
	mvn spring-boot:run

test:
	mvn test

up:
	docker compose up -d

down:
	docker compose down

logs:
	docker compose logs -f mysql

db-backup:
	docker exec -it $(MYSQL_CONTAINER) \
	mysqldump -u $(DB_USER) -p$(DB_PASS) $(DB_NAME) --no-tablespaces > $(BACKUP)

db-restore:
	docker exec -i $(MYSQL_CONTAINER) \
	mysql -u $(DB_USER) -p$(DB_PASS) $(DB_NAME) < $(BACKUP)

db-shell:
	docker exec -it $(MYSQL_CONTAINER) \
	mysql -u $(DB_USER) -p$(DB_PASS)

clean:
	rm -rf target
