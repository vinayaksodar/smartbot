const builder = require('botbuilder');
const restify = require('restify');
 
//create the bot
const connector = new builder.ChatConnector();
const bot = new builder.UniversalBot(
    connector,
    [
        (session)=>{
            builder.Prompts.text(session,'Hello! What is your name?');
        } ,
        (session, results)=>{
            session.endDialog('Hello ' +results.response);
        }
    ]
);
//create the host web server
const server =restify.createServer();
server.post('/api/messages',connector.listen());
server.listen(
    process.env.PORT || 3978,
    function() {console.log('Server up!!')}
)
