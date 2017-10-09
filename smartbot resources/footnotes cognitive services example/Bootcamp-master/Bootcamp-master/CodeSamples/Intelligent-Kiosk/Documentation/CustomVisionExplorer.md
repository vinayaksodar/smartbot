# Custom Vision Explorer

This example shows how to use the Custom Vision APIs to create image classifiers and how to score images against a target classifier. 

Once you have it configured and at least one project created, just select a target project and send an image for scoring. You can do 
so from local files, Bing Image Search results or from a web camera capture. Once you submit a photo, the results are shown on above 
the photo as a list of tags and their associated confidence level. 

![alt text](https://github.com/Microsoft/Cognitive-Samples-IntelligentKiosk/blob/master/Documentation/CustomVisionExplorer.JPG "Custom Vision Explorer")

# Custom Vision Setup

This is where you can create and manage your own classifiers. In case you have used the Face Identification Setup in the kiosk this 
will look very familiar to you. It integrates with Bing Images very nicely, which literally makes the task of creating a classifier a 
matter of seconds. 

You can get to the Custom Vision Setup page by clicking on the ‘+’ button near the Project selector in the Custom Vision Explorer page. 
The Setup page will prompt you for API keys. In case you don’t have keys for the service you can find the key acquisition steps by 
clicking on the Settings button on the top-right of the Setup page.

![alt text](https://github.com/Microsoft/Cognitive-Samples-IntelligentKiosk/blob/master/Documentation/CustomVisionSetup.JPG "Custom Vision Setup")

# Camera integration during the Custom Vision Setup workflow

When you are creating your image classifiers the web cam capture can come in very handy. You can just enable auto-capture in the UI 
and spend a few seconds moving the object in front of the camera while it captures photos from several angles.

![alt text](https://github.com/Microsoft/Cognitive-Samples-IntelligentKiosk/blob/master/Documentation/CustomVisionCameraCapture.JPG "Custom Vision Camera Capture")

# Active Learning

It is very easy to create and train a project to do image classification on popular subjects (literally a few seconds as it integrates 
nicely with Bing Image searches). Scoring against these models using Bing Images works well, but if you use a web camera capture 
for scoring it is easy to run into issues. Between the other stuff around an object, or the unique lighting and POV from web camera 
captures, things can be very different than the images used in the trained set. 

As such, a good way to enhance a classifier is by correcting any missed result for images used during scoring. When you score an image
against one of your projects, a message will be displayed above the results (red arrow below) that allows you to use that image, along 
with the corrected result that you provide, as an input signal back into your project. All you do is provide the proper tags and the 
kiosk does the rest – it will add the image to the proper tag(s) in your project and trigger a re-train of the project. 

Here is the UI that pops up when you enter the Active Learning UI. Just toggle all the tags that should be associated with the image 
and click the big “Save and re-train the project” button.

![alt text](https://github.com/Microsoft/Cognitive-Samples-IntelligentKiosk/blob/master/Documentation/CustomVisionActiveLearning.JPG "Custom Vision Active Learning")

As of now you can only use this workflow to correct images that still belong to at least one of the tags in the project. If you need to 
correct images that shouldn't belong to any tags, the current approach is to create an Other tag an associate such images to that tag.

# Key Source Code

* [CustomVisionExplorer](../Kiosk/Views/CustomVision/CustomVisionExplorer.xaml.cs): Main page that drives the demo. It hosts the CameraControl (see below) to display the live camera feed and allow photo captures, as well as the ImageSearchUserControl as another input model.

* [CameraControl](../Kiosk/Controls/CameraControl.xaml.cs): The code that contains the camera feed and enables camera captures.

* [ImageSearchUserControl](../Kiosk/Controls/ImageSearchUserControl.xaml.cs): A control used for providing input images. It allows you to provide images from Bing Images, local images or the web camera.
