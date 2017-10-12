require('dotenv-extended').load();
const builder = require('botbuilder');
const restify = require('restify');
var url = require('url');
var validUrl = require('valid-url');
var captionService = require('./caption-service');
var needle=require('needle');

//connector
var connector = new builder.ChatConnector({
    appId: process.env.MICROSOFT_APP_ID,
    appPassword: process.env.MICROSOFT_APP_PASSWORD
});


// Create your bot with a function to receive messages from the user.
// This default message handler is invoked if the user's utterance doesn't
// match any intents handled by other dialogs.
var bot = new builder.UniversalBot(connector, function (session, args) {
    session.send("Hi... I'm the note bot sample. I can create new notes, read saved notes to you and delete notes.");

   // If the object for storing notes in session.userData doesn't exist yet, initialize it
   if (!session.userData.notes) {
       session.userData.notes = {};
       console.log("initializing userData.notes in default message handler");
   }
});

// Add global LUIS recognizer to bot
var luisAppUrl = process.env.LUIS_APP_URL || 'https://westus.api.cognitive.microsoft.com/luis/v2.0/apps/6c9ee1ff-9254-4973-a0b1-880fe3ba209f?subscription-key=6f1e0acff86449f2af00ac9f0c8eb822&timezoneOffset=0&verbose=true&q=';
bot.recognizer(new builder.LuisRecognizer(luisAppUrl));


//Comuper vision linking
bot.dialog('recognise image',[
    function(session){
        builder.Prompts.attachment(session,'Sure upload the image or give me a link');
    },
    function (session, args, next) {
       
        if (hasImageAttachment(session)) {
            var stream = getImageStreamFromMessage(session.message);
            captionService
                .getCaptionFromStream(stream)
                .then(function (caption) { handleSuccessResponse(session, caption); })
                .catch(function (error) { handleErrorResponse(session, error); });
        } else {
            var imageUrl = session.message.text || (validUrl.isUri(session.message.text) ? session.message.text : null);
            if (imageUrl) {
                captionService
                    .getCaptionFromUrl(imageUrl)
                    .then(function (caption) { handleSuccessResponse(session, caption); })
                    .catch(function (error) { handleErrorResponse(session, error); });
            } else {
                session.send('Did you upload an image? I\'m more of a visual person. Try sending me an image or an image URL');
            }
        }
    },    
]).triggerAction({ 
    matches: 'cv',
    confirmPrompt: "This will cancel recognition of the image you started. Are you sure?" 
}).cancelAction('cancelCreateNote', "Note canceled.", {
    matches: /^(cancel|nevermind)/i,
    confirmPrompt: "Are you sure?"
});


// required functions for image recognition
function hasImageAttachment(session) {
    return session.message.attachments.length > 0 &&
        session.message.attachments[0].contentType.indexOf('image') !== -1;
}

function getImageStreamFromMessage(message) {
    var headers = {};
    var attachment = message.attachments[0];
    if (checkRequiresToken(message)) {
        // The Skype attachment URLs are secured by JwtToken,
        // you should set the JwtToken of your bot as the authorization header for the GET request your bot initiates to fetch the image.
        // https://github.com/Microsoft/BotBuilder/issues/662
        connector.getAccessToken(function (error, token) {
            var tok = token;
            headers['Authorization'] = 'Bearer ' + token;
            headers['Content-Type'] = 'application/octet-stream';

            return needle.get(attachment.contentUrl, { headers: headers });
        });
    }

    headers['Content-Type'] = attachment.contentType;
    return needle.get(attachment.contentUrl, { headers: headers });
}

function checkRequiresToken(message) {
    return message.source === 'skype' || message.source === 'msteams';
}

/**
 * Gets the href value in an anchor element.
 * Skype transforms raw urls to html. Here we extract the href value from the url
 * @param {string} input Anchor Tag
 * @return {string} Url matched or null
 */
function parseAnchorTag(input) {
    var match = input.match('^<a href=\"([^\"]*)\">[^<]*</a>$');
    if (match && match[1]) {
        return match[1];
    }

    return null;
}

//=========================================================
// Response Handling
//=========================================================
function handleSuccessResponse(session, caption) {
    if (caption) {
        session.send('I think it\'s ' + caption);
    }
    else {
        session.send('Couldn\'t find a caption for this one');
    }

}

function handleErrorResponse(session, error) {
    var clientErrorMessage = 'Oops! Something went wrong. Try again later.';
    if (error.message && error.message.indexOf('Access denied') > -1) {
        clientErrorMessage += "\n" + error.message;
    }

    console.error(error);
    session.send(clientErrorMessage);
}



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

