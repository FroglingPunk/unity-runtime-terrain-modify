using UnityEngine;

namespace TerrainModify.Example
{
    public class MovementController : MonoBehaviour
    {
        public float movementSpeed = 3f;
        public float rotationSpeed = 90f;


        private void Update()
        {
            var charController = GetComponent<CharacterController>();

            var inputVertical = Input.GetAxis("Vertical");
            var inputHorizontal = Input.GetAxis("Horizontal");

            var movement = Vector3.zero;
            movement.z = inputVertical * Time.deltaTime * movementSpeed;

            movement = transform.TransformDirection(movement);
            movement.y = -9.81f * Time.deltaTime;
            charController.Move(movement);

            transform.Rotate(0, inputHorizontal * Time.deltaTime * rotationSpeed, 0);
        }
    }
}