void setup() {
  pinMode(2, INPUT_PULLUP);
  pinMode(9, OUTPUT);
  Serial.begin(115200);
}
void loop() {
  int v = analogRead(A0);
  analogWrite(9, v / 4);
  Serial.print("A0=");
  Serial.print(v);
  Serial.print(" D2=");
  Serial.println(digitalRead(2));
  delay(100);
}
