// See https://github.com/dialogflow/dialogflow-fulfillment-nodejs
// for Dialogflow fulfillment library docs, samples, and to report issues
'use strict';
 
const functions = require('firebase-functions');
const bent = require('bent');
const getJSON = bent('json');
const {WebhookClient} = require('dialogflow-fulfillment');
const {Card, Suggestion} = require('dialogflow-fulfillment');
 
process.env.DEBUG = 'dialogflow:debug'; // enables lib debugging statements
 
exports.dialogflowFirebaseFulfillment = functions.https.onRequest((request, response) => {
  const agent = new WebhookClient({ request, response });
  console.log('Dialogflow Request headers: ' + JSON.stringify(request.headers));
  console.log('Dialogflow Request body: ' + JSON.stringify(request.body));
 
  function welcome(agent) {
    agent.add(`Welcome to my agent!`);
  }
 
  function fallback(agent) {
    agent.add(`I didn't understand`);
    agent.add(`I'm sorry, can you try again?`);
  }
  
  function worldwideStats (agent){
    const type = agent.parameters.type;
    return getJSON("https://coronavirus-tracker-api.herokuapp.com/v2/latest?source=jhu").then(result => {
        showWorlwideData(type, result.latest);
    });
  }
  
  function showWorlwideData(caseTypes, resultSet){
    let output = 'According to my latest data, there are ';
    if (caseTypes.length === 1 && caseTypes[0] === 'all') { caseTypes = ['confirmed', 'deaths', 'recovered']; }
    caseTypes.forEach((caseType, index, caseTypes) => {
        output = `${output}${resultSet[caseType]} ${caseType}`;
        output = `${(caseType !== 'deaths') ? `${output} cases` : `${output}`}`;
        if(caseTypes.length > 1) output = `${output}, `;
    });
    output = `${output} worldwide.`;
    agent.add(`${output}`);
  }
  
  function locationStats (agent) {
    const { county, country, dateTime, province, type:caseTypes } = agent.parameters;
    let src = '', countryCode = '', countyName = '', provinceName = '';
    
    // if county, or province exists in query, this means we are requesting US data,
    // else, we are requesting country level data of some other nation.
    if(county.length || province.length) {
        src = 'nyt';
        countryCode = 'US';
        countyName = county.length === 1 ? county[0].split(' ')[0].trim() : '';
        provinceName = province.length === 1 ? province[0] : '';
    } else {
        src = 'jhu';
        // if more than 1 country exist, then we dont set country code, else we set country code.
        countryCode = country.length === 1 ? country[0]['alpha-2'] : '';
    }
    
    let queryParams = `${src}`;
    if (dateTime) queryParams = `${queryParams}&timelines=true`;
    // if countyName exist, then province must be present and country must be US.
    if (countyName) {
        queryParams = `${queryParams}&county=${countyName}&province=${provinceName}&country_code=US`;
    } else if (provinceName) {
        // if provinceName exist, country must be US.
        queryParams = `${queryParams}&province=${provinceName}&country_code=US`;
    } else if (countryCode) {
        queryParams = `${queryParams}&country_code=${countryCode}`;
    }
    
    const url = `https://coronavirus-tracker-api.herokuapp.com/v2/locations?source=${queryParams}`;
    return getJSON(url).then(result => {
        const datetimeRange = {};
        let resultSet = {};
        let outputTime = '';

        // when dateTime values exist.
        if(dateTime) {
            if(typeof(dateTime) === 'string') {
                // yesterday, specific date
                datetimeRange.startDate = new Date(dateTime).getTime();
                datetimeRange.endDate = new Date().getTime();
                outputTime = `since ${new Date(dateTime).toLocaleString()}`;
            } else {
                datetimeRange.startDate = new Date(dateTime.startDate).getTime();
                datetimeRange.endDate = new Date(dateTime.endDate).getTime();
                outputTime = `between ${new Date(dateTime.startDate).toLocaleString()} and ${new Date(dateTime.endDate).toLocaleString()}`;
            }
        }

        // case 1. When >= 1 county exist.
        if (county.length) {
            // we must know the province here and country must be US.
            const countyNames = county.map((countyName) => countyName.toLowerCase().split(' ')[0].trim());
            // we filter counties in a certain province.
            resultSet = result.locations.filter((location) =>
                location.province.toLowerCase() === provinceName.toLowerCase() && (countyNames).indexOf(location.county.toLowerCase()) !== -1 )
                .reduce((previousVal, currentVal) => {
                    const { county, timelines, latest } = currentVal;
                    previousVal[county] = dateTime ? getCaseCountByTimelines(timelines, datetimeRange) : latest;
                    return previousVal;
                }, {});
          	// adding the word 'County', 'parish' back to result set for display.
            const countyResults = {};
            for(const key in resultSet) {
                const countyName = county.find((countyName) => countyName.includes(key));
                countyResults[countyName] = resultSet[key];
            }
            resultSet = countyResults;
        }

        // case 2. When >= 1 province exist.
        else if (province.length) {
            // country must be US.
            const provinceNames = province.map((provinceName) => provinceName.toLowerCase());
            // we filter provinces within US.
            resultSet = result.locations.filter((location) => location.country_code === 'US' && (provinceNames).indexOf(location.province.toLowerCase()) !== -1 )
                .reduce((previousVal, currentVal) => {
                    const { province:provinceName, timelines, latest } = currentVal;
                    if (previousVal.hasOwnProperty(provinceName)) {
                        Object.keys(previousVal[provinceName]).forEach((type) => {
                            previousVal[provinceName][type] = previousVal[provinceName][type] + (dateTime ? getCaseCountByTimelines(timelines, datetimeRange)[type] : latest[type]);
                        });
                    } else {
                        previousVal[provinceName] = dateTime ? getCaseCountByTimelines(timelines, datetimeRange) : latest;
                    }
                    return previousVal;
                }, {});
        } 

        // case 3. When >= country exist.
        else if(country.length) {
            const countryCodes = country.map((countryObj)=> countryObj['alpha-2']);
            // we filter countries based on country codes and group by country names.
            resultSet = result.locations.filter((location) => (countryCodes).indexOf(location.country_code) !== -1 ).reduce((previousVal, currentVal) => {
                const { country: countryName, latest, timelines } = currentVal;
                if (previousVal.hasOwnProperty(countryName)) {
                    Object.keys(previousVal[countryName]).forEach((type) => {
                        previousVal[countryName][type] = previousVal[countryName][type] + (dateTime ? getCaseCountByTimelines(timelines, datetimeRange)[type] : latest[type]);
                    });
                } else {
                    previousVal[countryName] = dateTime ? getCaseCountByTimelines(timelines, datetimeRange) : latest;
                }
                return previousVal;
            }, {});
        }
        
        // show data.
        showData(caseTypes, resultSet, `${outputTime}`);
    });
  }
  
  function getCaseCountByTimelines(timelines, datetimeRange) {
    const count = {};
    // for each case type,
    Object.keys(timelines).forEach((caseType) => {
        const newValues = [];
        // we get set of values 
        const origTimeValues = timelines[caseType].timeline;
        for (const origTimestamp in origTimeValues) {
            if (origTimeValues.hasOwnProperty(origTimestamp)) {
                const availableTime = new Date(origTimestamp).getTime();
                // between case. (example) yesterday, last week, last month, last 3 months
                if(availableTime >= datetimeRange.startDate && availableTime <= datetimeRange.endDate) {
                    newValues.push(origTimeValues[origTimestamp]);
                }
            }
        }
        count[caseType] = (newValues.length > 1 ? newValues[newValues.length -1] - newValues[0] : 0);
    });
    return count;
  }
  
  function showData(caseTypes, resultSet, outputTime){
    let output = 'According to my latest data, there are ';
    if (caseTypes.length === 1 && caseTypes[0] === 'all') { caseTypes = ['confirmed', 'deaths', 'recovered']; }
    Object.keys(resultSet).forEach((location, index, locations)=>{
        caseTypes.forEach((caseType, index, caseTypes) => {
            output = `${output}${resultSet[location][caseType]} ${caseType}`;
            output = `${(caseType !== 'deaths') ? `${output} cases` : `${output}`}`;
            if(caseTypes.length > 1) output = `${output}, `;
        });
        output = `${output} in ${location}`;
        if(locations.length > 1) output = `${output}, `;
    });
    if(outputTime) output = `${output} ${outputTime}`;
    agent.add(`${output}`);
  }

  // Run the proper function handler based on the matched Dialogflow intent name
  let intentMap = new Map();
  intentMap.set('Default Welcome Intent', welcome);
  intentMap.set('Default Fallback Intent', fallback);
  intentMap.set('Worldwide Stats Intent', worldwideStats);
  intentMap.set('Location Stats Intent', locationStats);
  agent.handleRequest(intentMap);
});
