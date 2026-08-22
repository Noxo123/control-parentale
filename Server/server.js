const express = require('express');
const path = require('path');
const Database = require('better-sqlite3');

const app = express();
const PORT = Number(process.env.PORT || 20570);
const HOST = '127.0.0.1';
app.use(express.json({limit:'64kb'}));
app.use(express.static(path.join(__dirname,'public')));

const db = new Database(path.join(__dirname,'parental.db'));
db.pragma('journal_mode = WAL');
db.exec(`
CREATE TABLE IF NOT EXISTS settings (
 id INTEGER PRIMARY KEY CHECK(id=1), parent_pin TEXT NOT NULL,
 daily_limit_minutes INTEGER NOT NULL DEFAULT 120,
 start_time TEXT NOT NULL DEFAULT '08:00', end_time TEXT NOT NULL DEFAULT '21:00'
);
CREATE TABLE IF NOT EXISTS blocked_apps (
 id INTEGER PRIMARY KEY AUTOINCREMENT, process_name TEXT NOT NULL UNIQUE,
 enabled INTEGER NOT NULL DEFAULT 1
);
CREATE TABLE IF NOT EXISTS events (
 id INTEGER PRIMARY KEY AUTOINCREMENT, type TEXT NOT NULL,
 message TEXT NOT NULL, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);`);
if (!db.prepare('SELECT id FROM settings WHERE id=1').get()) {
 db.prepare("INSERT INTO settings(id,parent_pin) VALUES(1,?)").run(process.env.NOXO_PARENT_PIN || '1234');
}
const getSettings=()=>db.prepare('SELECT id,daily_limit_minutes,start_time,end_time FROM settings WHERE id=1').get();
const log=(type,message)=>db.prepare('INSERT INTO events(type,message) VALUES(?,?)').run(type,message);
function auth(req,res,next){
 const pin=db.prepare('SELECT parent_pin FROM settings WHERE id=1').get().parent_pin;
 if(req.get('X-Parent-Pin')!==pin)return res.status(401).json({error:'PIN parent invalide'});
 next();
}
app.get('/api/status',auth,(req,res)=>res.json({ok:true,service:'Noxo Parental Control',time:new Date().toISOString()}));
app.get('/api/settings',auth,(req,res)=>res.json(getSettings()));
app.post('/api/settings',auth,(req,res)=>{
 const daily=Number(req.body.daily_limit_minutes),start=String(req.body.start_time||''),end=String(req.body.end_time||'');
 if(!Number.isInteger(daily)||daily<1||daily>1440)return res.status(400).json({error:'Limite invalide'});
 if(!/^([01]\\d|2[0-3]):[0-5]\\d$/.test(start)||!/^([01]\\d|2[0-3]):[0-5]\\d$/.test(end))return res.status(400).json({error:'Horaires invalides'});
 db.prepare('UPDATE settings SET daily_limit_minutes=?,start_time=?,end_time=? WHERE id=1').run(daily,start,end);
 log('settings',`Paramètres modifiés : ${daily} min, ${start}-${end}`);res.json(getSettings());
});
app.post('/api/pin',auth,(req,res)=>{
 const pin=String(req.body.pin||'');
 if(!/^\\d{4,12}$/.test(pin))return res.status(400).json({error:'PIN invalide'});
 db.prepare('UPDATE settings SET parent_pin=? WHERE id=1').run(pin);log('security','PIN parent modifié');res.json({ok:true});
});
app.get('/api/blocked-apps',auth,(req,res)=>res.json(db.prepare('SELECT * FROM blocked_apps ORDER BY process_name').all()));
app.post('/api/blocked-apps',auth,(req,res)=>{
 let name=String(req.body.process_name||'').trim().toLowerCase().replace(/\\.exe$/i,'');
 if(!/^[a-z0-9._-]{1,80}$/.test(name))return res.status(400).json({error:'Nom de processus invalide'});
 try{const r=db.prepare('INSERT INTO blocked_apps(process_name) VALUES(?)').run(name);log('blocklist',`Application bloquée : ${name}.exe`);res.json(db.prepare('SELECT * FROM blocked_apps WHERE id=?').get(r.lastInsertRowid));}
 catch{res.status(409).json({error:'Déjà présente'});}
});
app.delete('/api/blocked-apps/:id',auth,(req,res)=>{
 const item=db.prepare('SELECT * FROM blocked_apps WHERE id=?').get(Number(req.params.id));if(!item)return res.status(404).json({error:'Introuvable'});
 db.prepare('DELETE FROM blocked_apps WHERE id=?').run(item.id);log('blocklist',`Application retirée : ${item.process_name}.exe`);res.json({ok:true});
});
app.get('/api/events',auth,(req,res)=>{const limit=Math.min(Math.max(Number(req.query.limit)||100,1),500);res.json(db.prepare('SELECT * FROM events ORDER BY id DESC LIMIT ?').all(limit));});
app.get('/api/agent-config',(req,res)=>{const s=getSettings();res.json({...s,blocked_apps:db.prepare('SELECT process_name FROM blocked_apps WHERE enabled=1').all().map(x=>x.process_name)});});
app.post('/api/agent-event',(req,res)=>{const message=String(req.body.message||'').slice(0,500);if(!message)return res.status(400).json({error:'message manquant'});log(String(req.body.type||'agent'),message);res.json({ok:true});});
app.get('*',(req,res)=>res.sendFile(path.join(__dirname,'public','index.html')));
app.listen(PORT,HOST,()=>console.log(`Noxo Parental Control: http://${HOST}:${PORT}`));
