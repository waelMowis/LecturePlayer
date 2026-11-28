// حساسات: ضوء A0، حرارة A1، مسافة (TRIG 12، ECHO 11)، حركة 7، مفتاح 4
const float BETA = 3950;
void setup() { pinMode(12, OUTPUT); pinMode(11, INPUT); pinMode(7, INPUT); pinMode(4, INPUT); Serial.begin(115200); }
void loop() {
  digitalWrite(12, LOW); delayMicroseconds(2); digitalWrite(12, HIGH); delayMicroseconds(10); digitalWrite(12, LOW);
  long us = pulseIn(11, HIGH, 30000);
  int t = analogRead(A1);
  float c = 1 / (log(1 / (1023. / t - 1)) / BETA + 1.0 / 298.15) - 273.15;
  Serial.print("L="); Serial.print(analogRead(A0));
  Serial.print(" T="); Serial.print(c, 1);
  Serial.print(" D="); Serial.print(us / 58);
  Serial.print(" M="); Serial.print(digitalRead(7));
  Serial.print(" S="); Serial.println(digitalRead(4));
  delay(100);
}
