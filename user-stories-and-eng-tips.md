# User stories

The user stories below are the high level goals to achieve for the project.

1. Have video preview and playback
- I want to be able to upload a video and be able to play the video, drag a selector on the video timeline, pause, etc. with any other video player.

2. Being able to crop video lengths
- I want to be able to intuitvely crop the video lengths by dragging two selectors on the ends of the video timeline.
- When dragging the video ends/selectors, I would like the video preview/playback to show me where my selector is corresponding to in the video, so that I know where to place the selector.

3. Being able to export the video
- I would like export my cropped video
- I want options when exporting my video, such as resolution, compression/bitrate, FPS, video format, etc. It's okay to keep the aspect ratio and framing of the video as is, I don't want to crop spacially, just the time of the video

4. Targeted export size
- In addition to all of the above, I want an optional "targeted size" feature, where the program will determine the compression needed so that my video will be able to reach a specified file size or lower based on the provided resolution, FPS, and video format. This will allow me to send my videos on platforms that have size restrictions (e.g. 10MB, 5MB, etc.)

5. Audio export settings
- I would also like a section in the export menu to choose some audio settings, such as bitrate or excluding the audio.

6. GIF conversion
- In addition to video format, I want an option to convert my video into a GIF.
- In the GIF conversion menu, I would also like the option to pick my compression levels, resolution, fps, and also targeted file size.

# Engineering tips

The engineering tips are the more technical goals on what should be achieved in terms of implementing the program.

- Use WinUI3 to make this application.
- For the video editing algorithm/backend of the program, use FFMPEG bindings, or propose any other existing and lightweight bindings.
- Prioritize lightweight and fast responsive application. The goal is to achieve the user stories in the simplest and most straight forward way in terms of code.
- With the idea of lightweight and responsive application, keep the UI/UX style simple, but intuitive. No need for any custom styling, use the default WinUI styles or even just pure barebones and no styling is fine; just make sure the UI/UX is intuitive and *positioning* of UI components are appropriate.
