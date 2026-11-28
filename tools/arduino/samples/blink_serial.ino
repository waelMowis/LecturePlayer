// وميض + الشاشة التسلسلية
int n = 0;
void setup() {
  pinMode(13, OUTPUT);
  Serial.begin(9600);
  Serial.println("مرحبا من أردوينو");
}
void loop() {
  digitalWrite(13, HIGH);
  delay(250);
  digitalWrite(13, LOW);
  delay(250);
  Serial.print("tick ");
  Serial.println(n++);
}
