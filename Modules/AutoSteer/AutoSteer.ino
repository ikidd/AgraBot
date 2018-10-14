#include "BNO055_AOG.h"
#include <Wire.h>

#define A 0X28  //I2C address selection pin LOW
#define B 0x29  //                        HIGH
#define RAD2GRAD 57.2957795

//timer 2 controls 9,10
  #define WORKSW_PIN 4  //PD4
  #define STEERSW_PIN 5 //PD5
  #define RELAY1_PIN 6  //PD6
  #define   DIR_PIN  7  //PD7
  //pin 8 is ether
  #define   PWM_PIN  9  //PB1  
  //pin 10 is anothe timer 2 pwm
  
  //ethercard 8,11,12,13   

//instance of the imu
BNO055 IMU(A);

//loop time variables in microseconds
const unsigned int LOOP_TIME = 100; //100hz
unsigned int lastTime = LOOP_TIME;
unsigned int currentTime = LOOP_TIME;
unsigned int dT = 50000;
unsigned int count = 0;
byte watchdogTimer = 0;
byte serialResetTimer = 0; //if serial buffer is getting full, empty it

//Kalman variables
float rollK = 0, Pc = 0.0, G = 0.0, P = 1.0, Xp = 0.0, Zp = 0.0;
float XeRoll = 0;
const float varRoll = 0.1; // variance,
const float varProcess = 0.0001; //smaller is more filtering
/*
float setK = 0, setPc = 0.0, setG = 0.0, setP = 1.0, setXp = 0.0, setZp = 0.0;
float setXe = 0;
const float setVariance = 0.1; // variance,
const float setProcess = 0.001; //smaller is more filtering
*/

 //program flow
bool isDataFound = false, isSettingFound = false;
int header = 0, tempHeader = 0, temp;

byte relay = 0, uTurn = 0, speeed = 0, workSwitch = 0, steerSwitch = 1, switchByte = 0;
float distanceFromLine = 0, corr = 0;

//steering variables
float steerAngleActual = 0;
int steerPrevSign = 0, steerCurrentSign = 0; // the steering wheels angle currently and previous one
float steerAngleSetPoint = 0; //the desired angle from AgOpen
int steeringPosition = 0, steeringPositionZero = 512; //from steering sensor
float steerAngleError = 0; //setpoint - actual
float distanceError = 0; //
float steerSensorCounts = 4;

//inclinometer variables
int roll = 0;

//pwm variables
int pwmDrive = 0, drive = 0, pwmDisplay = 0;
float pValue = 0, iValue = 0, dValue = 0;
byte minPWMValue = 10;

//PID variables
float Ko = 0.0f;  //overall gain
float Kp = 0.0f;  //proportional gain
float Ki = 0.0f;//integral gain
float Kd = 0.0f;  //derivative gain

//integral values - **** change as required *****
float   maxIntErr = 200; //anti windup max
float maxIntegralValue = 20; //max PWM value for integral PID component

//error values
float lastError = 0, lastLastError = 0, integrated_error = 0, dError = 0;

void setup()
{
  pinMode(DIR_PIN, OUTPUT); //D12 PB3 direction pin of PWM Board
    
  //keep pulled high and drag low to activate, noise free safe    
  pinMode(WORKSW_PIN, INPUT_PULLUP);   //Pin D4 PD4
  pinMode(STEERSW_PIN, INPUT_PULLUP);  //Pin 11 PB2	pinMode(RELAY1_PIN, OUTPUT); //configure RELAY1 for output //Pin 5
	pinMode(RELAY1_PIN, OUTPUT); //configure RELAY2 for output //Pin 6
	pinMode(DIR_PIN, OUTPUT); //D11 PB3 direction pin of PWM Board

	//set up communication
	Wire.begin();
	Serial.begin(38400);
	IMU.init();

	// Restore calibration data from zeroing
	bno055_offsets_t calibrationData;

	calibrationData.accel_offset_x = 65520;
	calibrationData.accel_offset_y = 65421;
	calibrationData.accel_offset_z = 65421;
	calibrationData.gyro_offset_x = 65534;
	calibrationData.gyro_offset_y = 65533;
	calibrationData.gyro_offset_z = 1;
	calibrationData.mag_offset_x = 65305;
	calibrationData.mag_offset_y = 119;
	calibrationData.mag_offset_z = 65418;
	calibrationData.accel_radius = 1000;
	calibrationData.mag_radius = 979;

	IMU.setSensorOffsets(calibrationData);

	//use external 32K crystal
	IMU.setExtCrystalUse(true);

	//PWM rate settings Adjust to desired PWM Rate
	//TCCR1B = TCCR1B & B11111000 | B00000010;    // set timer 1 divisor to     8 for PWM frequency of  3921.16 Hz
	TCCR1B = TCCR1B & B11111000 | B00000011;    // set timer 1 divisor to    64 for PWM frequency of   490.20 Hz (The DEFAULT)
}

void loop()
{
	/*
	 * Loop triggers every 100 msec and sends back gyro heading, and roll, steer angle etc
	 * All imu code goes in the loop
	 *  Determine the header value and set the flag accordingly
	 *  Then the next group of serial data is according to the flag
	 *  Process accordingly updating values
	 */

	currentTime = millis();
	unsigned int time = currentTime;

	if (currentTime - lastTime >= LOOP_TIME)
	{
		dT = currentTime - lastTime;
		lastTime = currentTime;

		IMU.readIMU();

		//If connection lost to AgOpenGPS, the watchdog will count up and turn off steering
		if (watchdogTimer++ > 250) watchdogTimer = 12;

		//clean out serial buffer to prevent buffer overflow
		if (serialResetTimer++ > 20)
		{
			while (Serial.available() > 0) char t = Serial.read();
			serialResetTimer = 0;
		}

		// kalman SINGLE VARIABLE process
		rollK = IMU.euler.roll;

		//inclinometer
		delay(1);
		analogRead(A1); //discard
		delay(1);
		roll = analogRead(A1);   delay(2);
		roll += analogRead(A1);   delay(2);
		roll += analogRead(A1);   delay(2);
		roll += analogRead(A1);
		roll = roll >> 2; //divide by 4

		//inclinometer goes from -25 to 25 from 0.5 volts to 4.5 volts
		rollK = map(roll, 0, 1023, -500, 500); //20 counts per degree * 16.0
		rollK *= 0.8;

		//Kalman filter
		Pc = P + varProcess;
		G = Pc / (Pc + varRoll);
		P = (1 - G) * Pc;
		Xp = XeRoll;
		Zp = Xp;
		XeRoll = G * (rollK - Zp) + Xp;

		workSwitch = digitalRead(WORKSW_PIN);  // read work switch
		steerSwitch = digitalRead(STEERSW_PIN); //read auto steer enable switch open = 0n closed = Off
    switchByte = 0;
		switchByte = steerSwitch << 1; //put steerswitch status in bit 1 position
		switchByte = workSwitch | switchByte;

	SetRelays(); //turn on off sections

		//steering position and steer angle
		analogRead(A0); //discard initial reading
		steeringPosition = analogRead(A0);    delay(2);
		steeringPosition += analogRead(A0);    delay(2);
		steeringPosition += analogRead(A0);    delay(2);
		steeringPosition += analogRead(A0);
		steeringPosition = steeringPosition >> 2; //divide by 4
		steeringPosition = (steeringPosition - steeringPositionZero + (XeRoll * (Kd/24)) );   //read the steering position sensor
		//steeringPosition = ( steeringPosition - steeringPositionZero);   //read the steering position sensor

    //close enough to center, remove any correction
    if (distanceFromLine <= 40 && distanceFromLine >= -40) corr = 0;
    else
    {
      //use the integal value to adjust how much per cycle it increases
      corr +=Ki;

      //provide a limit - the old max integral value
      if (corr > maxIntegralValue) corr = maxIntegralValue;

      //now add the correction to fool steering position
      if (distanceFromLine > 40) 
      {
        steerAngleSetPoint -= corr;
      }
      else
      {
        steerAngleSetPoint += corr;
      }
    }
    
		//convert position to steer angle. 6 counts per degree of steer pot position in my case
		//  ***** make sure that negative steer angle makes a left turn and positive value is a right turn *****
		// remove or add the minus for steerSensorCounts to do that.
		steerAngleActual = (float)(steeringPosition) / -steerSensorCounts;

    //Kalman filter for setpoint
   /* setPc = setP + setProcess;
    setG = setPc / (setPc + setVariance);
    setP = (1 - setG) * setPc;
    setXp = setXe;
    setZp = setXp;
    setXe = setG * (steerAngleSetPoint - setZp) + setXp;
    */

		if (watchdogTimer < 10)
		{
			steerAngleError = steerAngleActual - steerAngleSetPoint;   //calculate the steering error
			calcSteeringPID();  //do the pid
			motorDrive();       //out to motors the pwm value
		
   
		else
		{
			//we've lost the comm to AgOpenGPS
			pwmDrive = 0; //turn off steering motor
			motorDrive(); //out to motors the pwm value
		}

		//Send to agopenGPS **** you must send 5 numbers ****
		Serial.print(steerAngleActual); //The actual steering angle in degrees
		Serial.print(",");
		Serial.print(steerAngleSetPoint);   //the pwm value to solenoids or motor
		Serial.print(",");

		// *******  if there is no gyro installed send 9999
		//Serial.print(9999); //heading in degrees * 16
		Serial.print(IMU.euler.head); //heading in degrees * 16
		Serial.print(",");

		//*******  if no roll is installed, send 9999
		//Serial.print((9999); //roll in degrees * 16
		Serial.print((int)XeRoll); //roll in degrees * 16
		Serial.print(",");

		Serial.println(uTurn); //steering switch status

		Serial.flush();   // flush out buffer
	} //end of timed loop

	  //****************************************************************************************
	  //This runs continuously, outside of the timed loop, keeps checking UART for new data
	  // header high/low, relay byte, speed byte, high distance, low distance, Steer high, steer low
	if (Serial.available() > 0 && !isDataFound && !isSettingFound) //find the header, 127H + 254L = 32766
	{
		int temp = Serial.read();
		header = tempHeader << 8 | temp;               //high,low bytes to make int
		tempHeader = temp;                             //save for next time
		if (header == 32766) isDataFound = true;     //Do we have a match?
		if (header == 32764) isSettingFound = true;     //Do we have a match?
	}

	//Data Header has been found, so the next 6 bytes are the data
	if (Serial.available() > 6 && isDataFound)
	{
		isDataFound = false;
		relay = Serial.read();   // read relay control from AgOpenGPS
		speeed = Serial.read() >> 2;  //actual speed times 4, single byte

		//distance from the guidance line in mm
		distanceFromLine = (float)(Serial.read() << 8 | Serial.read());   //high,low bytes

		//set point steer angle * 10 is sent
		steerAngleSetPoint = ((float)(Serial.read() << 8 | Serial.read()))*0.01; //high low bytes

		//auto Steer is off if 32020,Speed is too slow, motor pos or footswitch open
		if (distanceFromLine == 32020 | speeed < 1 | steerSwitch == 1)
		{
			watchdogTimer = 12;//turn off steering motor
		}
		else          //valid conditions to turn on autosteer
		{
			//bitSet(PINB, 5);   //turn LED on
			watchdogTimer = 0;  //reset watchdog
			serialResetTimer = 0; //if serial buffer is getting full, empty it
		}

    //uturn byte read in
    uTurn = Serial.read();
	}

	//Settings Header has been found, 8 bytes are the settings
	if (Serial.available() > 7 && isSettingFound)
	{
		isSettingFound = false;  //reset the flag

		//change the factors as required for your own PID values
		Kp = (float)Serial.read() * 1.0;   // read Kp from AgOpenGPS
		Ki = (float)Serial.read() * 0.001;   // read Ki from AgOpenGPS
		Kd = (float)Serial.read() * 1.0;   // read Kd from AgOpenGPS
		Ko = (float)Serial.read() * 0.1;   // read Ko from AgOpenGPS
		steeringPositionZero = 385 + Serial.read();  //read steering zero offset
		minPWMValue = Serial.read(); //read the minimum amount of PWM for instant on
		maxIntegralValue = Serial.read()*0.1; //
		steerSensorCounts = Serial.read(); //sent as 10 times the setting displayed in AOG
	}
}
