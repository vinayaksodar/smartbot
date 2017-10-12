const builder = require('botbuilder');
const restify = require('restify');

//connector
var connector = new builder.ChatConnector({
    appId: process.env.MICROSOFT_APP_ID,
    appPassword: process.env.MICROSOFT_APP_PASSWORD
});


// Create your bot with a function to receive messages from the user.
// This default message handler is invoked if the user's utterance doesn't
// match any intents handled by other dialogs.
var bot = new builder.UniversalBot(connector, function (session, args) {
    session.send("Hi... I'm smartbot. I can create new notes, read saved notes to you and delete notes.");

   // If the object for storing notes in session.userData doesn't exist yet, initialize it
   if (!session.userData.notes) {
       session.userData.notes = {};
       console.log("initializing userData.notes in default message handler");
   }
});

// Add global LUIS recognizer to bot
var luisAppUrl = process.env.LUIS_APP_URL || 'https://westus.api.cognitive.microsoft.com/luis/v2.0/apps/6c9ee1ff-9254-4973-a0b1-880fe3ba209f?subscription-key=6f1e0acff86449f2af00ac9f0c8eb822&timezoneOffset=0&verbose=true&q=';
bot.recognizer(new builder.LuisRecognizer(luisAppUrl));

//greeting dialog
bot.dialog('hello', [
    function (session, args, next) {
        // Resolve and store any Note.Title entity passed from LUIS.
        /*var intent = args.intent;
        var title = builder.EntityRecognizer.findEntity(intent.entities, 'Note.Title');

        var note = session.dialogData.note = {
          title: title ? title.entity : null,
        */
        
        // Prompt for title
        session.send("Hi... ");
        builder.Prompts.text(session, 'What is your name?');
    
        
        
    },
    function (session, results, next) {
        //var note = session.dialogData.note;
        if (results.response) {
            session.endDialog('Hello ' +results.response);
        }
        builder.Prompts.text(session,"How could i help you?");
        
    
    }
]).triggerAction({ 
    matches: 'greeting',
});

//applinking
bot.dialog('connect', [
    function (session, args, next) {
        // Resolve and store any Note.Title entity passed from LUIS.
        /*var intent = args.intent;
        var title = builder.EntityRecognizer.findEntity(intent.entities, 'Note.Title');

        var note = session.dialogData.note = {
          title: title ? title.entity : null,
        */
        
        // Prompt for title
        
        builder.Prompts.text(session, 'Oops! Could not connect');
    
        
        
    },
    
]).triggerAction({ 
    matches: 'applinking',
});

//singing
bot.dialog('sing', [
    function (session, args, next) {
        // Resolve and store any Note.Title entity passed from LUIS.
        /*var intent = args.intent;
        var title = builder.EntityRecognizer.findEntity(intent.entities, 'Note.Title');

        var note = session.dialogData.note = {
          title: title ? title.entity : null,
        */
        
        // Prompt for title
        
        builder.Prompts.text(session, 'Haha you would not like it');
    
        
        
    },
    
]).triggerAction({ 
    matches: 'sing',
});

//comparison dialog
bot.dialog('better than', [
    function (session, args, next) {
        
        var intent = args.intent;
        var com = builder.EntityRecognizer.findEntity(intent.entities, 'comparison');
        
        
        

        var note = session.dialogData.note = {
          com: com ? com.entity : null,
        };
        
        // Prompt for title
        if (!note.com) {
            builder.Prompts.text(session, 'I am a bot in the initial stages! hence I cannot be compared to them');
        } else {
            next();
        }
    }
        
]).triggerAction({ 
    matches: 'personal',

});

//knowledge dialog
bot.dialog('know', [
    function (session, args, next) {
        // Resolve and store any Note.Title entity passed from LUIS.
        /*var intent = args.intent;
        var title = builder.EntityRecognizer.findEntity(intent.entities, 'Note.Title');

        var note = session.dialogData.note = {
          title: title ? title.entity : null,
        */
        
        
        builder.Prompts.text(session, 'Not yet! But i am learning...');
    
        
        
    },
    
]).triggerAction({ 
    matches: 'knowledge',
});

//identity dialog
bot.dialog('made', [
    function (session, args, next) {
       
        builder.Prompts.text(session, 'I am smartbot!!');
    
        
        
    },
    
]).triggerAction({ 
    matches: 'identity',
});

//greeting dialog
bot.dialog('created', [
    function (session, args, next) {
        // Resolve and store any Note.Title entity passed from LUIS.
        /*var intent = args.intent;
        var title = builder.EntityRecognizer.findEntity(intent.entities, 'Note.Title');

        var note = session.dialogData.note = {
          title: title ? title.entity : null,
        */
        
       
        builder.Prompts.text(session, 'Well i dont really know them but am grateful that they made me');
    
        
        
    },
    
]).triggerAction({ 
    matches: 'developer',
});

// CreateNote dialog
bot.dialog('CreateNote', [
    function (session, args, next) {
        // Resolve and store any Note.Title entity passed from LUIS.
        var intent = args.intent;
        var title = builder.EntityRecognizer.findEntity(intent.entities, 'Note.Title');

        var note = session.dialogData.note = {
          title: title ? title.entity : null,
        };
        
        // Prompt for title
        if (!note.title) {
            builder.Prompts.text(session, 'What would you like to call your note?');
        } else {
            next();
        }
    },
    function (session, results, next) {
        var note = session.dialogData.note;
        if (results.response) {
            note.title = results.response;
        }

        // Prompt for the text of the note
        if (!note.text) {
            builder.Prompts.text(session, 'What would you like to say in your note?');
        } else {
            next();
        }
    },
    function (session, results) {
        var note = session.dialogData.note;
        if (results.response) {
            note.text = results.response;
        }
        
        // If the object for storing notes in session.userData doesn't exist yet, initialize it
        if (!session.userData.notes) {
            session.userData.notes = {};
            console.log("initializing session.userData.notes in CreateNote dialog");
        }
        // Save notes in the notes object
        session.userData.notes[note.title] = note;

        // Send confirmation to user
        session.endDialog('Creating note named "%s" with text "%s"',
            note.title, note.text);
    }
]).triggerAction({ 
    matches: 'Note.Create',
    confirmPrompt: "This will cancel the creation of the note you started. Are you sure?" 
}).cancelAction('cancelCreateNote', "Note canceled.", {
    matches: /^(cancel|nevermind)/i,
    confirmPrompt: "Are you sure?"
});

// Delete note dialog
bot.dialog('DeleteNote', [
    function (session, args, next) {
        if (noteCount(session.userData.notes) > 0) {
            // Resolve and store any Note.Title entity passed from LUIS.
            var title;
            var intent = args.intent;
            var entity = builder.EntityRecognizer.findEntity(intent.entities, 'Note.Title');
            if (entity) {
                // Verify that the title is in our set of notes.
                title = builder.EntityRecognizer.findBestMatch(session.userData.notes, entity.entity);
            }
            
            // Prompt for note name
            if (!title) {
                builder.Prompts.choice(session, 'Which note would you like to delete?', session.userData.notes);
            } else {
                next({ response: title });
            }
        } else {
            session.endDialog("No notes to delete.");
        }
    },
    function (session, results) {
        delete session.userData.notes[results.response.entity];        
        session.endDialog("Deleted the '%s' note.", results.response.entity);
    }
]).triggerAction({
    matches: 'Note.Delete'
}).cancelAction('cancelDeleteNote', "Ok - canceled note deletion.", {
    matches: /^(cancel|nevermind)/i
});



// Helper function to count the number of notes stored in session.userData.notes
function noteCount(notes) {

    var i = 0;
    for (var name in notes) {
        i++;
    }
    return i;
}

// Read note dialog
bot.dialog('ReadNote', [
    function (session, args, next) {
        if (noteCount(session.userData.notes) > 0) {
           
            // Resolve and store any Note.Title entity passed from LUIS.
            var title;
            var intent = args.intent;
            var entity = builder.EntityRecognizer.findEntity(intent.entities, 'Note.Title');
            if (entity) {
                // Verify it's in our set of notes.
                title = builder.EntityRecognizer.findBestMatch(session.userData.notes, entity.entity);
            }
            
            // Prompt for note name
            if (!title) {
                builder.Prompts.choice(session, 'Which note would you like to read?', session.userData.notes);
            } else {
                next({ response: title });
            }
        } else {
            session.endDialog("No notes to read.");
        }
    },
    function (session, results) {        
        session.endDialog("Here's the '%s' note: '%s'.", results.response.entity, session.userData.notes[results.response.entity].text);
    }
]).triggerAction({
    matches: 'Note.ReadAloud'
}).cancelAction('cancelReadNote', "Ok.", {
    matches: /^(cancel|nevermind)/i
});



//create the host web server
const server =restify.createServer();
server.post('/api/messages',connector.listen());
server.listen(
    process.env.PORT || 3978,
    function() {console.log('Server up!!')}
)

