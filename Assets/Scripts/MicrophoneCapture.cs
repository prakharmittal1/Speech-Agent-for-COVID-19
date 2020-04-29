/**
Based on code by https://github.com/ITP-xStory/uniFlow.
Modified by Jaime Ruiz on 4/2020 to enable dynamic access key generation and other
fixes.
Use at your own risk.
*/
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using JsonData;
using System;
using UnityEngine.UI;
using UnityEngine.Networking;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
 

[RequireComponent(typeof(AudioSource))]

public class MicrophoneCapture : MonoBehaviour
{
    /**
    These are the variables that you need to change depending on your file.
    It is easisest if the certificate is in the Assets Dir.
    **/
    const string PROJECT_ID = "nuispring-dfrobf";
    const string email = "nuilecture-564@nuispring-dfrobf.iam.gserviceaccount.com";
    /* Make sure it is in the Assets Folder so that this is only the name */
    string certPath = "nuispring2020.p12";

    // This needs to be changed to access you script
    public ChadBehavior myScript;

    private Context[] contexts;
    private string ACCESS_TOKEN;

    //A boolean that flags whether there's a connected microphone
    private bool micConnected = false;

    //The maximum and minimum available recording frequencies
    private int minFreq;
    private int maxFreq;

    //A handle to the attached AudioSource
    private AudioSource goAudioSource;

    //Public variable for saving recorded sound clip
    public AudioClip recordedClip;
    private float[] samples;
    private byte[] bytes;
    //dialogflow
    private AudioSource audioSource;
    private readonly object thisLock = new object();
    private volatile bool recordingActive;


    //Use this for initialization
    void Start()
    {
        this.certPath = System.IO.Path.Combine(new string[] { Application.dataPath, this.certPath });
        StartCoroutine(this.GetAccessToken());

        this.myScript = GameObject.FindObjectOfType(typeof(ChadBehavior)) as ChadBehavior;

        //Check if there is at least one microphone connected
        if (Microphone.devices.Length <= 0)
        {
            //Throw a warning message at the console if there isn't
            Debug.LogWarning("Microphone not connected!");
        }
        else //At least one microphone is present
        {
            //Set 'micConnected' to true
            micConnected = true;

            //Get the default microphone recording capabilities
            Microphone.GetDeviceCaps(null, out minFreq, out maxFreq);

            //According to the documentation, if minFreq and maxFreq are zero, the microphone supports any frequency...
            if (minFreq == 0 && maxFreq == 0)
            {
                //...meaning 44100 Hz can be used as the recording sampling rate
                maxFreq = 44100;
            }

            //Get the attached AudioSource component
            goAudioSource = this.GetComponent<AudioSource>();
        }

    }

    IEnumerator GetAccessToken()
    {
        string jwt = GoogleJsonWebToken.GetJwt(
                email, certPath, GoogleJsonWebToken.SCOPE);
        WWW tokenRequest = GoogleJsonWebToken.GetAccessTokenRequest(jwt);
        yield return tokenRequest;

        if (tokenRequest.error == null)
        {
            var deserializedResponse = JObject.Parse(tokenRequest.text);
            this.ACCESS_TOKEN = (string)deserializedResponse["access_token"];
            Debug.Log("Access Token Obtained: " + this.ACCESS_TOKEN);
        }
        else
        {
            Debug.Log("ERROR: " + tokenRequest.text);
        }
    }

    void OnGUI()
    {
        //If there is a microphone
        if (micConnected)
        {
            //If the audio from any microphone isn't being captured
            if (!Microphone.IsRecording(null))
            {
                //Case the 'Record' button gets pressed
                if (GUI.Button(new Rect(Screen.width / 3 - -128, Screen.height / 4 - 30, 150, 40), "Ask Me Anything"))
                {
                    //Start recording and store the audio captured from the microphone at the AudioClip in the AudioSource
                    //goAudioSource.clip = Microphone.Start(null, true, 20, maxFreq);
                    //recordedClip = goAudioSource.clip;
                    //samples = new float[goAudioSource.clip.samples];
                    //handle dialogflow
                    StartListening(goAudioSource);
                }
            }
            else //Recording is in progress
            {
                //Case the 'Stop and Play' button gets pressed
                if (GUI.Button(new Rect(Screen.width / 4 - 80, Screen.height / 4 - 45, 150, 40), "Recording.."))
                {
                    //Microphone.End(null); //Stop the audio recording
                    //goAudioSource.Play(); //Playback the recorded audio
                    //Debug.Log(recordedClip.length);
                    //send out request
                    StopListening();
                }

                GUI.Label(new Rect(Screen.width / 4 - 100, Screen.height / 4 + 25, 200, 50), ".");
            }
        }
        else // No microphone
        {
            //Print a red "Microphone not connected!" message at the center of the screen
            GUI.contentColor = Color.red;
            GUI.Label(new Rect(Screen.width / 4 - 100, Screen.height / 4 - 25, 200, 50), "Microphone not connected!");
        }

    }

    public void StartListening(AudioSource audioSource)
    {
        lock (thisLock)
        {
            if (!recordingActive)
            {
                this.audioSource = audioSource;
                StartRecording();
            }
            else
            {
                Debug.LogWarning("Can't start new recording session while another recording session active");
            }
        }
    }

    private void StartRecording()
    {
        audioSource.clip = Microphone.Start(null, true, 20, 16000);
        recordingActive = true;

        //FireOnListeningStarted();
    }

    public void StopListening()
    {
        if (recordingActive)
        {

            //float[] samples = null;

            lock (thisLock)
            {
                if (recordingActive)
                {
                    StopRecording();
                    //samples = new float[audioSource.clip.samples];

                    //audioSource.clip.GetData(samples, 0);
                    bytes = WavUtil.FromAudioClip(audioSource.clip);
                    // audioSource.Play();
                    // Debug.Log("This is the audiosource clip length: " + bytes.Length);
                    audioSource = null;
                }
            }

            //new Thread(StartVoiceRequest).Start(samples);
            StartCoroutine(StartVoiceRequest("https://dialogflow.googleapis.com/v2/projects/" + PROJECT_ID + "/agent/sessions/34563:detectIntent",
                ACCESS_TOKEN,
                bytes));
        }
    }

    private void StopRecording()
    {
        Microphone.End(null);
        recordingActive = false;
    }

    IEnumerator StartVoiceRequest(String url, String AccessToken, object parameter)
    {
        byte[] samples = (byte[])parameter;
        //TODO: convert float[] samples into bytes[]
        //byte[] sampleByte = new byte[samples.Length * 4];
        //Buffer.BlockCopy(samples, 0, sampleByte, 0, sampleByte.Length);

        string sampleString = System.Convert.ToBase64String(samples);
        if (samples != null)
        {
            UnityWebRequest postRequest = new UnityWebRequest(url, "POST");
            RequestBody requestBody = new RequestBody();
            requestBody.queryInput = new QueryInput();
            requestBody.queryInput.audioConfig = new InputAudioConfig();
            requestBody.queryInput.audioConfig.audioEncoding = AudioEncoding.AUDIO_ENCODING_UNSPECIFIED;
            //TODO: check if that the sample rate hertz
            requestBody.queryInput.audioConfig.sampleRateHertz = 16000;
            requestBody.queryInput.audioConfig.languageCode = "en";
            requestBody.inputAudio = sampleString;


            string jsonRequestBody = JsonUtility.ToJson(requestBody, true);
            Debug.Log(jsonRequestBody);

            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonRequestBody);
            postRequest.SetRequestHeader("Authorization", "Bearer " + AccessToken);
            postRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            postRequest.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
            //postRequest.SetRequestHeader("Content-Type", "application/json");

            yield return postRequest.SendWebRequest();

            if (postRequest.isNetworkError || postRequest.isHttpError)
            {
                Debug.Log(postRequest.responseCode);
                Debug.Log(postRequest.error);
            }
            else
            {

                Debug.Log("Response: " + postRequest.downloadHandler.text);

                // Or retrieve results as binary data
                byte[] resultbyte = postRequest.downloadHandler.data;
                string result = System.Text.Encoding.UTF8.GetString(resultbyte);
                ResponseBody content = (ResponseBody)JsonUtility.FromJson<ResponseBody>(result);

                byte[] wavBytes = System.Convert.FromBase64String(content.outputAudio);
                AudioClip audioClip = WavUtil.ToAudioClip(wavBytes, 0);
                StartCoroutine(myScript.WaitForEnd(audioClip));
                this.contexts = content.queryResult.outputContexts;
                Debug.Log(content.queryResult.fulfillmentText);
            }
        }
        else
        {
            Debug.LogError("The audio file is null");
        }
    }

}
