# embeddingsearch
**This is still highly work-in-progress**

Embeddingsearch is a python library that uses Embedding Similarity Search (similiarly to [Magna](https://github.com/yousef-rafat/Magna/tree/main)) to semantically compare a given input to a database of pre-processed entries.

When first implementing the idea, it was conceptualized to only import files into the database.

# How to set up
1. Install [ollama](https://ollama.com/download)
2. Pull a few models using ollama (e.g. `paraphrase-multilingual`, `bge-m3`, `mxbai-embed-large`, `nomic-embed-text`)
3. [Install the depencencies](#installing-the-dependencies)
4. [Set up a local mysql database](#mysql-database-setup)

# How to run the example script
1. Start the script `python3 dbtest.py`
2. Generate the index. Type in `index_folder` and submit. Then `target` and submit. (This might take a while with no GPU acceleration - go get some coffee)
3. After the indexing is done, you may prompt searches using `search`

# Installing the dependencies
## Ubuntu 24.04
`pip install mysql.connector`
`apt install python3-magic`
## Windows
TODO

# MySQL database setup
1. Install mysql: `sudo apt install mysql-server` and connect to it: `sudo mysql -u root`
1. Create the database
`CREATE DATABASE embeddingsearch; use embeddingsearch;`
2. Create the user
`CREATE USER embeddingsearch identified by "somepassword!"; GRANT ALL ON embeddingsearch.* TO embeddingsearch;`
3. Create the tables
```sql
CREATE TABLE searchdomain (id int PRIMARY KEY auto_increment, name varchar(512), settings JSON);

CREATE TABLE query (id int PRIMARY KEY auto_increment, id_searchdomain int, query TEXT, FOREIGN KEY (id_searchdomain) REFERENCES searchdomain(id));

CREATE TABLE entity (id int PRIMARY KEY auto_increment, name varchar(512), probmethod varchar(128), id_searchdomain int, FOREIGN KEY (id_searchdomain) REFERENCES searchdomain(id));

CREATE TABLE queryresult (id int PRIMARY KEY auto_increment, id_query int, id_entity int, result double, FOREIGN KEY (id_query) REFERENCES query(id), FOREIGN KEY (id_entity) REFERENCES entity(id));

CREATE TABLE attribute (id int PRIMARY KEY auto_increment, id_entity int, attribute varchar(512), value longtext, FOREIGN KEY (id_entity) REFERENCES entity(id));

CREATE TABLE datapoint (id int PRIMARY KEY auto_increment, name varchar(512), probmethod_embedding varchar(512), id_entity int, FOREIGN KEY (id_entity) REFERENCES entity(id));

CREATE TABLE embedding (id int PRIMARY KEY auto_increment, id_datapoint int, model varchar(512), embedding blob, FOREIGN KEY (id_datapoint) REFERENCES datapoint(id));
```

# To-do
- Implement the api server (WSGI via gunicorn / falcon)
- Move the models to the db file and move functions into the corresponding classes. (Maybe if circular references can be avoided, move them back to the model file in the end?)
- Add database setup script?
- Remove tables related to caching (It's not done on the sql server side anymore.)
- Improve performance (Create ready-to-go processes where each contain an n'th share of the entity cache, ready to perform a query. Prepare it after creating the entity cache.)
- Perhaps split the database code into a "read-only" library, optimized for query performance and caching, and a management library meant for updating the cache?

# Off-scope
- Support for other database types
