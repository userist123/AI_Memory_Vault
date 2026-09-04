import {AuthService} from"../service/authService.js"
import {AuthController} from "../Controller/authController.js"
import mongoUserRepository from "../repository/UserRepository.js"

class Container {
    static init(){

        const repositories = {
            userRepository: mongoUserRepository
        };

        const services = {
            authService: new AuthService(repositories.userRepository)
        };

        const controllers = {
            authController: new AuthController(services.authService)
        }

        return {
            repositories, services, controllers
        }
    }
}

const initialized = Container.init();
export {Container};
export default initialized;