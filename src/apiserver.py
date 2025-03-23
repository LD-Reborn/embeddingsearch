
import falcon
import falcon.request
import db
import util
import configparser

CONFIG_APIKEYS = ["0eeb46b2-064c-11f0-b1e8-87363427365e", "1a4ec916-064c-11f0-ac90-ff442f509128"]

def key_check(req:falcon.Request) -> bool:
    try:
        key = req.params['key']
        return key in CONFIG_APIKEYS
    except:
        return False

def key_denied(req:falcon.Request, resp:falcon.Response):
    resp.status = 401
    resp.media = {"error": "unauthorized", "reason": "Invalid api key or key not given. /path?key=yourkey"}

class SearchDomain:
    def on_get(self, req:falcon.Request, resp:falcon.Response) -> None:
        if not key_check(req): return key_denied(req, resp)
        try:
            verb = req.params['verb']
        except:
            verb = ""
        match verb:
            case "create": # e.g. http://127.0.0.1:8000/searchdomain?key=0eeb46b2-064c-11f0-b1e8-87363427365e&verb=create
                resp.status = 200
                resp.media = {"error": "not implemented yet", "reason": "give me a minute :3"}
            case "update":
                pass
            case "list":
                pass
            case "get":
                pass
            case "delete" | "remove" | "rm":
                pass
            case _:
                resp.status = 418
                resp.media = {"error": "not implemented yet", "reason": "unknown verb"}


# !!! Start this server using: gunicorn apiserver:app !!!

app = falcon.App()
app.add_route('/searchdomain', SearchDomain())





