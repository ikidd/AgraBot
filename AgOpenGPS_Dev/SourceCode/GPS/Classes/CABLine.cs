﻿using SharpGL;
using System;

namespace AgOpenGPS
{
    public class CABLine
    {
        //pid  steering
        public double kp = Properties.Settings.Default.pid_kp;
        public double ki = Properties.Settings.Default.pid_ki;
        public double kd = Properties.Settings.Default.pid_kd;
        public double distancefilter = 0;

        public double speedminlahead = Properties.Settings.Default.speedminlookahead;
        public double speedmaxlahead = Properties.Settings.Default.speedmaxlookahead;

        public bool iscabortner, iscabschelter, iscabsaving, iscabfix, iscabspeed,isfilter;

        public double p_error, i_error, d_error;
        public double cte;
        public double steerangelpid;
        double pre_cte;
        public double goalPointDistance;


        public double abHeading;
        public double abFixHeadingDelta;

        public bool isABSameAsVehicleHeading = true;
        public bool isOnRightSideCurrentLine = true;

        public double refLineSide = 1.0;

        public double distanceFromRefLine;
        public double distanceFromCurrentLine;
        public double snapDistance;

        public bool isABLineSet;
        public bool isABLineBeingSet;
        public double passNumber;

        public double howManyPathsAway;

        //tramlines
        //Color tramColor = Color.YellowGreen;
        public int tramPassEvery;

        public int passBasedOn;

        //pointers to mainform controls
        private readonly FormGPS mf;

        private readonly OpenGL gl;

        //the two inital A and B points
        public vec2 refPoint1 = new vec2(0.2, 0.2);

        public vec2 refPoint2 = new vec2(0.3, 0.3);

        //the reference line endpoints
        public vec2 refABLineP1 = new vec2(0.0, 0.0);

        public vec2 refABLineP2 = new vec2(0.0, 1.0);

        //the current AB guidance line
        public vec2 currentABLineP1 = new vec2(0.0, 0.0);

        public vec2 currentABLineP2 = new vec2(0.0, 1.0);

        //pure pursuit values
        public vec2 goalPointAB = new vec2(0, 0);

        public vec2 radiusPointAB = new vec2(0, 0);
        public double steerAngleAB;
        public double rEastAB, rNorthAB;
        public double ppRadiusAB;

        public CABLine(OpenGL _gl, FormGPS _f)
        {
            //constructor
            gl = _gl;
            mf = _f;
        }

        public void DeleteAB()
        {
            refPoint1 = new vec2(0.0, 0.0);
            refPoint2 = new vec2(0.0, 1.0);

            refABLineP1 = new vec2(0.0, 0.0);
            refABLineP2 = new vec2(0.0, 1.0);

            currentABLineP1 = new vec2(0.0, 0.0);
            currentABLineP2 = new vec2(0.0, 1.0);

            abHeading = 0.0;

            passNumber = 0.0;

            howManyPathsAway = 0.0;

            isABLineSet = false;
        }

        public void SetABLineByBPoint()
        {
            refPoint2.easting = mf.pn.fix.easting;
            refPoint2.northing = mf.pn.fix.northing;

            //calculate the AB Heading
            abHeading = Math.Atan2(refPoint2.easting - refPoint1.easting, refPoint2.northing - refPoint1.northing);
            if (abHeading < 0) abHeading += glm.twoPI;

            //sin x cos z for endpoints, opposite for additional lines
            refABLineP1.easting = refPoint1.easting - (Math.Sin(abHeading) * 4000.0);
            refABLineP1.northing = refPoint1.northing - (Math.Cos(abHeading) * 4000.0);

            refABLineP2.easting = refPoint1.easting + (Math.Sin(abHeading) * 4000.0);
            refABLineP2.northing = refPoint1.northing + (Math.Cos(abHeading) * 4000.0);

            isABLineSet = true;
        }

        public void SetABLineByHeading()
        {
            //heading is set in the AB Form
            refABLineP1.easting = refPoint1.easting - (Math.Sin(abHeading) * 4000.0);
            refABLineP1.northing = refPoint1.northing - (Math.Cos(abHeading) * 4000.0);

            refABLineP2.easting = refPoint1.easting + (Math.Sin(abHeading) * 4000.0);
            refABLineP2.northing = refPoint1.northing + (Math.Cos(abHeading) * 4000.0);

            refPoint2.easting = refABLineP2.easting;
            refPoint2.northing = refABLineP2.northing;

            isABLineSet = true;
        }

        public void SnapABLine()
        {
            double headingCalc;
            //calculate the heading 90 degrees to ref ABLine heading
            if (isOnRightSideCurrentLine) headingCalc = abHeading + glm.PIBy2;
            else headingCalc = abHeading - glm.PIBy2;

            //calculate the new points for the reference line and points
            refPoint1.easting = (Math.Sin(headingCalc) * Math.Abs(distanceFromCurrentLine) * 0.001) + refPoint1.easting;
            refPoint1.northing = (Math.Cos(headingCalc) * Math.Abs(distanceFromCurrentLine) * 0.001) + refPoint1.northing;

            refABLineP1.easting = refPoint1.easting - (Math.Sin(abHeading) * 4000.0);
            refABLineP1.northing = refPoint1.northing - (Math.Cos(abHeading) * 4000.0);

            refABLineP2.easting = refPoint1.easting + (Math.Sin(abHeading) * 4000.0);
            refABLineP2.northing = refPoint1.northing + (Math.Cos(abHeading) * 4000.0);

            refPoint2.easting = refABLineP2.easting;
            refPoint2.northing = refABLineP2.northing;
        }

        public double angVel;

        public void GetCurrentABLine(vec3 pivot)
        {
            //move the ABLine over based on the overlap amount set in vehicle
            double widthMinusOverlap = mf.vehicle.toolWidth - mf.vehicle.toolOverlap;

            //x2-x1
            double dx = refABLineP2.easting - refABLineP1.easting;
            //z2-z1
            double dy = refABLineP2.northing - refABLineP1.northing;

            //how far are we away from the reference line at 90 degrees
            distanceFromRefLine = ((dy * pivot.easting) - (dx * pivot.northing) + (refABLineP2.easting
                                    * refABLineP1.northing) - (refABLineP2.northing * refABLineP1.easting))
                                        / Math.Sqrt((dy * dy) + (dx * dx));

            //sign of distance determines which side of line we are on
            if (distanceFromRefLine > 0) refLineSide = 1;
            else refLineSide = -1;

            //absolute the distance
            distanceFromRefLine = Math.Abs(distanceFromRefLine);

            //Which ABLine is the vehicle on, negative is left and positive is right side
            howManyPathsAway = Math.Round(distanceFromRefLine / widthMinusOverlap, 0, MidpointRounding.AwayFromZero);

            //generate that pass number as signed integer
            passNumber = Convert.ToInt32(refLineSide * howManyPathsAway);

            //calculate the new point that is number of implement widths over
            double toolOffset = mf.vehicle.toolOffset;
            vec2 point1;

            //depending which way you are going, the offset can be either side
            if (isABSameAsVehicleHeading)
            {
                point1 = new vec2((Math.Cos(-abHeading) * ((widthMinusOverlap * howManyPathsAway * refLineSide) - toolOffset)) + refPoint1.easting,
                (Math.Sin(-abHeading) * ((widthMinusOverlap * howManyPathsAway * refLineSide) - toolOffset)) + refPoint1.northing);
            }
            else
            {
                point1 = new vec2((Math.Cos(-abHeading) * ((widthMinusOverlap * howManyPathsAway * refLineSide) + toolOffset)) + refPoint1.easting,
                    (Math.Sin(-abHeading) * ((widthMinusOverlap * howManyPathsAway * refLineSide) + toolOffset)) + refPoint1.northing);
            }

            //create the new line extent points for current ABLine based on original heading of AB line
            currentABLineP1.easting = point1.easting - (Math.Sin(abHeading) * 40000.0);
            currentABLineP1.northing = point1.northing - (Math.Cos(abHeading) * 40000.0);

            currentABLineP2.easting = point1.easting + (Math.Sin(abHeading) * 40000.0);
            currentABLineP2.northing = point1.northing + (Math.Cos(abHeading) * 40000.0);

            //get the distance from currently active AB line
            //x2-x1
            dx = currentABLineP2.easting - currentABLineP1.easting;
            //z2-z1
            dy = currentABLineP2.northing - currentABLineP1.northing;

            //save a copy of dx,dy in youTurn
            mf.yt.dxAB = dx; mf.yt.dyAB = dy;

            //how far from current AB Line is fix
            distanceFromCurrentLine = ((dy * pivot.easting) - (dx * pivot.northing) + (currentABLineP2.easting
                        * currentABLineP1.northing) - (currentABLineP2.northing * currentABLineP1.easting))
                        / Math.Sqrt((dy * dy) + (dx * dx));

            //are we on the right side or not
            isOnRightSideCurrentLine = distanceFromCurrentLine > 0;

            //absolute the distance
            distanceFromCurrentLine = Math.Abs(distanceFromCurrentLine);

            //double goalPointDistance = mf.vehicle.goalPointLookAhead;
            //if (distanceFromCurrentLine < mf.vehicle.goalPointDistanceFromLine )
            //    goalPointDistance -= goalPointDistance * (mf.vehicle.goalPointDistanceFromLine - distanceFromCurrentLine);
            //if (goalPointDistance < mf.vehicle.goalPointLookAheadMinimum) goalPointDistance = mf.vehicle.goalPointLookAheadMinimum;


            if (isfilter)
            {
                distancefilter = ((9 * distancefilter) + distanceFromCurrentLine) / 10;
                distanceFromCurrentLine = distancefilter;
            }


            if (iscabortner)
            {

                goalPointDistance = (mf.pn.speed - distanceFromCurrentLine * mf.vehicle.goalPointLookAhead) * speedmaxlahead; // goalPointLookAhead should be 10-20

                if (distanceFromCurrentLine > 0.4)
                {
                    goalPointDistance = (mf.pn.speed - 0.4 * mf.vehicle.goalPointLookAhead);
                    goalPointDistance += (distanceFromCurrentLine - 0.4) * mf.vehicle.goalPointLookAhead * speedmaxlahead;

                    if (goalPointDistance > mf.pn.speed * speedmaxlahead) goalPointDistance = mf.pn.speed * speedmaxlahead;
                }

                if (goalPointDistance < speedminlahead) goalPointDistance = speedminlahead;
            }
            else if (iscabfix)
            {
                goalPointDistance = mf.pn.speed * mf.vehicle.goalPointLookAhead * 0.27777777;

                if (distanceFromCurrentLine < 1.0)
                    goalPointDistance += distanceFromCurrentLine * goalPointDistance * speedmaxlahead;
                else
                    goalPointDistance += goalPointDistance * speedmaxlahead;

                if (goalPointDistance < speedminlahead) goalPointDistance = speedminlahead;





            }
            else if (iscabspeed)
            {
                goalPointDistance = mf.pn.speed * speedmaxlahead;
                if (goalPointDistance < speedminlahead) goalPointDistance = speedminlahead;
            }

            else if (iscabschelter)
            {
                //!!!!!how far should goal point be away
                //!!!!!double goalPointDistance = (mf.pn.speed * mf.vehicle.goalPointLookAhead * 0.2777777777);
                //!!!!!if (goalPointDistance < mf.vehicle.minLookAheadDistance) goalPointDistance = mf.vehicle.minLookAheadDistance;


                //!!!!!Versuch: how far should goal point be away

                if (distanceFromCurrentLine < 0.3)
                {
                    goalPointDistance = (mf.pn.speed * mf.vehicle.goalPointLookAhead * 0.2777777777) - (distanceFromCurrentLine * 12.5 * (mf.pn.speed / 10));
                }

                if (distanceFromCurrentLine >= 0.3 & distanceFromCurrentLine < 0.6)
                {
                    goalPointDistance = (mf.pn.speed * mf.vehicle.goalPointLookAhead * 0.2777777777) - (((0.3 - (distanceFromCurrentLine - 0.3)) * 12.5 * (mf.pn.speed / 10)));
                }

                if (distanceFromCurrentLine >= 0.6 & distanceFromCurrentLine < 1)
                {
                    goalPointDistance = (mf.pn.speed * mf.vehicle.goalPointLookAhead * 0.2777777777);
                }

                if (distanceFromCurrentLine > 1 & distanceFromCurrentLine < 2.5)
                {
                    goalPointDistance = ((mf.pn.speed * mf.vehicle.goalPointLookAhead * 0.2777777777) * (((distanceFromCurrentLine - 1) / 10) + 1));
                }

                if (distanceFromCurrentLine >= 2.5)
                {
                    goalPointDistance = (mf.pn.speed * mf.vehicle.goalPointLookAhead * 0.2777777777) * 1.15;
                }



                //minimum of 3.0 meters look ahead
                if (goalPointDistance < speedminlahead) goalPointDistance = speedminlahead;





            }
            else
            {              //how far should goal point be away  - speed * seconds * kmph -> m/s then limit min value
                goalPointDistance = mf.pn.speed * mf.vehicle.goalPointLookAhead * 0.27777777;

                if (distanceFromCurrentLine < 1.0)
                    goalPointDistance += distanceFromCurrentLine * goalPointDistance * mf.vehicle.goalPointDistanceMultiplier;
                else
                    goalPointDistance += goalPointDistance * mf.vehicle.goalPointDistanceMultiplier;

                if (goalPointDistance < mf.vehicle.goalPointLookAheadMinimum) goalPointDistance = mf.vehicle.goalPointLookAheadMinimum;
            }



            
            mf.test1 = goalPointDistance;

            //Subtract the two headings, if > 1.57 its going the opposite heading as refAB
            abFixHeadingDelta = (Math.Abs(mf.fixHeading - abHeading));
            if (abFixHeadingDelta >= Math.PI) abFixHeadingDelta = Math.Abs(abFixHeadingDelta - glm.twoPI);

            // ** Pure pursuit ** - calc point on ABLine closest to current position
            double U = (((pivot.easting - currentABLineP1.easting) * dx)
                        + ((pivot.northing - currentABLineP1.northing) * dy))
                        / ((dx * dx) + (dy * dy));

            //point on AB line closest to pivot axle point
            rEastAB = currentABLineP1.easting + (U * dx);
            rNorthAB = currentABLineP1.northing + (U * dy);

            //how far should goal point be away  - speed * seconds * kmph -> m/s + min value
            //double goalPointDistance = (mf.pn.speed * mf.vehicle.goalPointLookAhead * 0.2777777777);
            //if (goalPointDistance < mf.vehicle.minLookAheadDistance) goalPointDistance = mf.vehicle.minLookAheadDistance;

            if (abFixHeadingDelta >= glm.PIBy2)
            {
                isABSameAsVehicleHeading = false;
                goalPointAB.easting = rEastAB - (Math.Sin(abHeading) * goalPointDistance);
                goalPointAB.northing = rNorthAB - (Math.Cos(abHeading) * goalPointDistance);
            }
            else
            {
                isABSameAsVehicleHeading = true;
                goalPointAB.easting = rEastAB + (Math.Sin(abHeading) * goalPointDistance);
                goalPointAB.northing = rNorthAB + (Math.Cos(abHeading) * goalPointDistance);
            }

            //calc "D" the distance from pivot axle to lookahead point
            double goalPointDistanceDSquared
                = glm.DistanceSquared(goalPointAB.northing, goalPointAB.easting, pivot.northing, pivot.easting);

            //calculate the the new x in local coordinates and steering angle degrees based on wheelbase
            double localHeading = glm.twoPI - mf.fixHeading;
            ppRadiusAB = goalPointDistanceDSquared / (2 * (((goalPointAB.easting - pivot.easting) * Math.Cos(localHeading))
                + ((goalPointAB.northing - pivot.northing) * Math.Sin(localHeading))));

            //make sure pp doesn't generate a radius smaller then turn radius
            //if (ppRadiusAB > 0)
            //{
            //    if (ppRadiusAB < mf.vehicle.minTurningRadius * 0.95) ppRadiusAB = mf.vehicle.minTurningRadius * 0.95;
            //}
            //else if (ppRadiusAB > -mf.vehicle.minTurningRadius * 0.95)
            //{
            //    ppRadiusAB = -mf.vehicle.minTurningRadius * 0.95;
            //}

            steerAngleAB = glm.toDegrees(Math.Atan(2 * (((goalPointAB.easting - pivot.easting) * Math.Cos(localHeading))
                + ((goalPointAB.northing - pivot.northing) * Math.Sin(localHeading))) * mf.vehicle.wheelbase
                / goalPointDistanceDSquared));
            if (steerAngleAB < -mf.vehicle.maxSteerAngle) steerAngleAB = -mf.vehicle.maxSteerAngle;
            if (steerAngleAB > mf.vehicle.maxSteerAngle) steerAngleAB = mf.vehicle.maxSteerAngle;

            //limit circle size for display purpose
            if (ppRadiusAB < -500) ppRadiusAB = -500;
            if (ppRadiusAB > 500) ppRadiusAB = 500;

            radiusPointAB.easting = pivot.easting + (ppRadiusAB * Math.Cos(localHeading));
            radiusPointAB.northing = pivot.northing + (ppRadiusAB * Math.Sin(localHeading));

            //Convert to millimeters
            distanceFromCurrentLine = Math.Round(distanceFromCurrentLine * 1000.0, MidpointRounding.AwayFromZero);

            //angular velocity in rads/sec  = 2PI * m/sec * radians/meters
            angVel = glm.twoPI * 0.277777 * mf.pn.speed * (Math.Tan(glm.toRadians(steerAngleAB))) / mf.vehicle.wheelbase;

            //clamp the steering angle to not exceed safe angular velocity
            if (Math.Abs(angVel) > mf.vehicle.maxAngularVelocity)
            {
                steerAngleAB = glm.toDegrees(steerAngleAB > 0 ? (Math.Atan((mf.vehicle.wheelbase * mf.vehicle.maxAngularVelocity)
                    / (glm.twoPI * mf.pn.speed * 0.277777)))
                    : (Math.Atan((mf.vehicle.wheelbase * -mf.vehicle.maxAngularVelocity) / (glm.twoPI * mf.pn.speed * 0.277777))));
            }

            //distance is negative if on left, positive if on right
            if (isABSameAsVehicleHeading)
            {
                if (!isOnRightSideCurrentLine) distanceFromCurrentLine *= -1.0;
            }

            //opposite way so right is left
            else
            {
                if (isOnRightSideCurrentLine) distanceFromCurrentLine *= -1.0;
            }



            //pid steering
            cte = distanceFromCurrentLine / 1000;
            pre_cte = p_error;

            p_error = cte;
            i_error += cte;
            d_error = cte - pre_cte;

            if (i_error > Properties.Settings.Default.pid_maxi_error) i_error = Properties.Settings.Default.pid_maxi_error;
            if (i_error < (Properties.Settings.Default.pid_maxi_error * -1.0)) i_error = Properties.Settings.Default.pid_maxi_error * -1.0;

            steerangelpid = -kp * p_error - ki * i_error - kd * d_error;
            //  steerangelpid *= 10;
            if (steerangelpid < -mf.vehicle.maxSteerAngle) steerangelpid = -mf.vehicle.maxSteerAngle;
            if (steerangelpid > mf.vehicle.maxSteerAngle) steerangelpid = mf.vehicle.maxSteerAngle;

            if (Math.Abs(angVel) > mf.vehicle.maxAngularVelocity)  //also for pid
            {
                steerangelpid = glm.toDegrees(steerangelpid > 0 ? (Math.Atan((mf.vehicle.wheelbase * mf.vehicle.maxAngularVelocity)
                    / (glm.twoPI * mf.pn.speed * 0.277777)))
                    : (Math.Atan((mf.vehicle.wheelbase * -mf.vehicle.maxAngularVelocity) / (glm.twoPI * mf.pn.speed * 0.277777))));
            }



           


            mf.guidanceLineDistanceOff = (Int16)distanceFromCurrentLine;

            if (Properties.Settings.Default.is_pidcontroller) mf.guidanceLineSteerAngle = (Int16)(steerangelpid * 100);
            else mf.guidanceLineSteerAngle = (Int16)(steerAngleAB * 100);
            ;

            if (mf.yt.isYouTurnShapeDisplayed)
            {
                //do the pure pursuit from youTurn
                mf.yt.DistanceFromYouTurnLine();

                //now substitute what it thinks are AB line values with auto turn values
                steerAngleAB = mf.yt.steerAngleYT;
                distanceFromCurrentLine = mf.yt.distanceFromCurrentLine;

                goalPointAB = mf.yt.goalPointYT;
                radiusPointAB.easting = mf.yt.radiusPointYT.easting;
                radiusPointAB.northing = mf.yt.radiusPointYT.northing;
                ppRadiusAB = mf.yt.ppRadiusYT;
            }
        }

        public void DrawABLines()
        {
            //Draw AB Points
            gl.PointSize(8.0f);
            gl.Begin(OpenGL.GL_POINTS);

            gl.Color(0.95f, 0.0f, 0.0f);
            gl.Vertex(refPoint1.easting, refPoint1.northing, 0.0);
            gl.Color(0.0f, 0.90f, 0.95f);
            gl.Vertex(refPoint2.easting, refPoint2.northing, 0.0);
            gl.End();
            gl.PointSize(1.0f);

            if (isABLineSet)
            {
                //Draw reference AB line
                gl.LineWidth(2);
                gl.Enable(OpenGL.GL_LINE_STIPPLE);
                gl.LineStipple(1, 0x07F0);
                gl.Begin(OpenGL.GL_LINES);
                gl.Color(0.49f, 0.25f, 0.37f);
                gl.Vertex(refABLineP1.easting, refABLineP1.northing, 0);
                gl.Vertex(refABLineP2.easting, refABLineP2.northing, 0);

                gl.End();
                gl.Disable(OpenGL.GL_LINE_STIPPLE);

                //draw current AB Line
                gl.LineWidth(3);
                gl.Begin(OpenGL.GL_LINES);
                gl.Color(0.9f, 0.0f, 0.0f);

                //calculate if tram line is here
                if (tramPassEvery != 0)
                {
                    int pass = (int)passNumber + (tramPassEvery * 300) - passBasedOn;
                    if (pass % tramPassEvery != 0) gl.Color(0.9f, 0.0f, 0.0f);
                    else gl.Color(0, 0.9, 0);
                }

                //based on line pass, make ref purple
                if (Math.Abs(passBasedOn - passNumber) < 0.0000000001 && tramPassEvery != 0) gl.Color(0.990f, 0.190f, 0.990f);

                gl.Vertex(currentABLineP1.easting, currentABLineP1.northing, 0.0);
                gl.Vertex(currentABLineP2.easting, currentABLineP2.northing, 0.0);
                gl.End();

                if (mf.isSideGuideLines)
                {
                    //get the tool offset and width
                    double toolOffset = mf.vehicle.toolOffset * 2;
                    double toolWidth = mf.vehicle.toolWidth - mf.vehicle.toolOverlap;

                    gl.Color(0.0f, 0.90f, 0.50f);
                    gl.LineWidth(1);
                    gl.Begin(OpenGL.GL_LINES);

                    //precalculate sin cos
                    double cosHeading = Math.Cos(-abHeading);
                    double sinHeading = Math.Sin(-abHeading);

                    if (isABSameAsVehicleHeading)
                    {
                        gl.Vertex((cosHeading * (toolWidth + toolOffset)) + currentABLineP1.easting, (sinHeading * (toolWidth + toolOffset)) + currentABLineP1.northing, 0);
                        gl.Vertex((cosHeading * (toolWidth + toolOffset)) + currentABLineP2.easting, (sinHeading * (toolWidth + toolOffset)) + currentABLineP2.northing, 0);
                        gl.Vertex((cosHeading * (-toolWidth + toolOffset)) + currentABLineP1.easting, (sinHeading * (-toolWidth + toolOffset)) + currentABLineP1.northing, 0);
                        gl.Vertex((cosHeading * (-toolWidth + toolOffset)) + currentABLineP2.easting, (sinHeading * (-toolWidth + toolOffset)) + currentABLineP2.northing, 0);

                        toolWidth *= 2;
                        gl.Vertex((cosHeading * toolWidth) + currentABLineP1.easting, (sinHeading * toolWidth) + currentABLineP1.northing, 0);
                        gl.Vertex((cosHeading * toolWidth) + currentABLineP2.easting, (sinHeading * toolWidth) + currentABLineP2.northing, 0);
                        gl.Vertex((cosHeading * (-toolWidth)) + currentABLineP1.easting, (sinHeading * (-toolWidth)) + currentABLineP1.northing, 0);
                        gl.Vertex((cosHeading * (-toolWidth)) + currentABLineP2.easting, (sinHeading * (-toolWidth)) + currentABLineP2.northing, 0);
                    }
                    else
                    {
                        gl.Vertex((cosHeading * (toolWidth - toolOffset)) + currentABLineP1.easting, (sinHeading * (toolWidth - toolOffset)) + currentABLineP1.northing, 0);
                        gl.Vertex((cosHeading * (toolWidth - toolOffset)) + currentABLineP2.easting, (sinHeading * (toolWidth - toolOffset)) + currentABLineP2.northing, 0);
                        gl.Vertex((cosHeading * (-toolWidth - toolOffset)) + currentABLineP1.easting, (sinHeading * (-toolWidth - toolOffset)) + currentABLineP1.northing, 0);
                        gl.Vertex((cosHeading * (-toolWidth - toolOffset)) + currentABLineP2.easting, (sinHeading * (-toolWidth - toolOffset)) + currentABLineP2.northing, 0);

                        toolWidth *= 2;
                        gl.Vertex((cosHeading * toolWidth) + currentABLineP1.easting, (sinHeading * toolWidth) + currentABLineP1.northing, 0);
                        gl.Vertex((cosHeading * toolWidth) + currentABLineP2.easting, (sinHeading * toolWidth) + currentABLineP2.northing, 0);
                        gl.Vertex((cosHeading * (-toolWidth)) + currentABLineP1.easting, (sinHeading * (-toolWidth)) + currentABLineP1.northing, 0);
                        gl.Vertex((cosHeading * (-toolWidth)) + currentABLineP2.easting, (sinHeading * (-toolWidth)) + currentABLineP2.northing, 0);
                    }
                    gl.End();
                }

                if (mf.isPureDisplayOn)
                {
                    //draw the guidance circle
                    const int numSegments = 100;
                    {
                        gl.Color(0.95f, 0.30f, 0.950f);
                        double theta = glm.twoPI / numSegments;
                        double c = Math.Cos(theta);//precalculate the sine and cosine
                        double s = Math.Sin(theta);

                        double x = ppRadiusAB;//we start at angle = 0
                        double y = 0;

                        gl.LineWidth(1);
                        gl.Begin(OpenGL.GL_LINE_LOOP);
                        for (int ii = 0; ii < numSegments; ii++)
                        {
                            //output vertex
                            gl.Vertex(x + radiusPointAB.easting, y + radiusPointAB.northing);

                            //apply the rotation matrix
                            double t = x;
                            x = (c * x) - (s * y);
                            y = (s * t) + (c * y);
                        }
                        gl.End();
                    }
                    //Draw lookahead Point
                    gl.PointSize(8.0f);
                    gl.Begin(OpenGL.GL_POINTS);
                    gl.Color(1.0f, 1.0f, 0.0f);
                    gl.Vertex(goalPointAB.easting, goalPointAB.northing, 0.0);
                    //gl.Color(0.6f, 0.95f, 0.95f);
                    //gl.Vertex(mf.at.rEastAT, mf.at.rNorthAT, 0.0);
                    //gl.Color(0.6f, 0.95f, 0.95f);
                    //gl.Vertex(mf.at.turnRadiusPt.easting, mf.at.turnRadiusPt.northing, 0.0);
                    gl.End();
                    gl.PointSize(1.0f);
                }

                if (mf.yt.isYouTurnShapeDisplayed)
                {
                    gl.Color(0.95f, 0.95f, 0.25f);
                    gl.LineWidth(2);
                    int ptCount = mf.yt.ytList.Count;
                    if (ptCount > 0)
                    {
                        gl.Begin(OpenGL.GL_LINE_STRIP);
                        for (int i = 0; i < ptCount; i++)
                        {
                            gl.Vertex(mf.yt.ytList[i].easting, mf.yt.ytList[i].northing, 0);
                        }
                        gl.End();
                    }

                    gl.Color(0.95f, 0.05f, 0.05f);
                }

                if (mf.yt.isRecordingCustomYouTurn)
                {
                    gl.Color(0.05f, 0.05f, 0.95f);
                    gl.PointSize(4.0f);
                    int ptCount = mf.yt.youFileList.Count;
                    if (ptCount > 1)
                    {
                        gl.Begin(OpenGL.GL_POINTS);
                        for (int i = 1; i < ptCount; i++)
                        {
                            gl.Vertex(mf.yt.youFileList[i].easting + mf.yt.youFileList[0].easting, mf.yt.youFileList[i].northing + mf.yt.youFileList[0].northing, 0);
                        }
                        gl.End();
                    }
                }

                gl.PointSize(1.0f);
                gl.LineWidth(1);
            }
        }

        public void ResetABLine()
        {
            refPoint1 = new vec2(0.2, 0.2);
            refPoint2 = new vec2(0.3, 0.3);

            refABLineP1 = new vec2(0.0, 0.0);
            refABLineP2 = new vec2(0.0, 1.0);

            currentABLineP1 = new vec2(0.0, 0.0);
            currentABLineP2 = new vec2(0.0, 1.0);

            abHeading = 0.0;
            isABLineSet = false;
            isABLineBeingSet = false;
            howManyPathsAway = 0.0;
            passNumber = 0;
        }
    }
}