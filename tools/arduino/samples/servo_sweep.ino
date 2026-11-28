#include <Servo.h>
Servo s;
void setup() { s.attach(9); Serial.begin(9600); }
void loop() {
  for (int a = 0; a <= 180; a += 45) { s.write(a); Serial.println(a); delay(300); }
}
